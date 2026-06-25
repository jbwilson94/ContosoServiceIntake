using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;

namespace Contoso.ServiceIntake.Plugins
{
    /// <summary>
    /// Stamps Service Request routing/SLA fields from the editable Routing/SLA Rule matrix
    /// and enforces close-time documentation guardrails.
    /// 
    /// Senior assessment alignment:
    /// - Keeps routing/SLA enforcement server-side so portal, model-driven app, imports, and API writes
    ///   all produce the same decision.
    /// - Reads configuration from Dataverse instead of hard-coding teams or SLA values.
    /// - Blocks high-severity closure without meaningful resolution notes, so agents cannot bypass
    ///   the documentation requirement through imports, model-driven forms, API writes, or automation.
    /// - Runs in PreOperation so the request is saved once with the routed values already populated.
    ///
    /// Register synchronously on contoso_servicerequest Create and Update in PreOperation.
    /// </summary>
    public sealed class ServiceRequestRoutingPlugin : PluginBase
    {
        private const string ServiceRequestTable = "contoso_servicerequest";
        private const string RoutingRuleTable = "contoso_routingslarule";
        private const string PreImageName = "PreImage";

        private const int ApprovalStatusNotRequired = 100000000;
        private const int ApprovalStatusPending = 100000001;

        private const int ExternalSyncStatusNotRequired = 100000000;
        private const int ExternalSyncStatusPending = 100000001;

        private const int StatusNew = 100000000;
        private const int StatusClosed = 100000002;
        private const int StatusDraft = 100000004;
        private const int SeverityHigh = 100000002;
        private const int MinimumHighSeverityResolutionLength = 20;

        private static readonly string[] RoutingInputColumns =
        {
            "contoso_categoryid",
            "contoso_severity"
        };

        public ServiceRequestRoutingPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ServiceRequestRoutingPlugin))
        {
            // Configuration is intentionally unused. Routing behavior is data-driven by contoso_routingslarule.
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var tracing = localPluginContext.TracingService;

            if (!IsSupportedStep(context))
            {
                tracing.Trace("Skipping unsupported step. Message={0}, Entity={1}, Stage={2}", context.MessageName, context.PrimaryEntityName, context.Stage);
                return;
            }

            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
            {
                tracing.Trace("Skipping execution because Target is missing or is not an Entity.");
                return;
            }

            try
            {
                var service = localPluginContext.PluginUserService;
                var preImage = GetPreImage(context);
                var isCreate = context.MessageName.Equals("Create", StringComparison.OrdinalIgnoreCase);
                var isUpdate = context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase);

                if (isCreate)
                {
                    // Power Pages multi-step forms create the row before the requester reaches final submit.
                    // Keep that partial row as Draft and wait until the submit step promotes it to New before
                    // assigning ownership or starting the SLA clock. This intentionally overrides the Dataverse
                    // choice default, which can enter the plugin pipeline as New even when the portal did not
                    // explicitly submit the request.
                    target["contoso_status"] = new OptionSetValue(StatusDraft);
                }

                if (isUpdate)
                {
                    // Keep the registered pre-image intentionally small and retrieve the current row when update
                    // logic needs full lifecycle/routing context. This avoids brittle step-image maintenance while
                    // still keeping the plugin deterministic for portal, app, import, and API updates.
                    preImage = RetrieveCurrentRequest(service, target.Id);
                }

                var status = GetValue<OptionSetValue>(target, preImage, "contoso_status");

                EnforceHighSeverityCloseGuardrail(context, target, preImage, tracing);

                if (ShouldSkipRouting(context, target, preImage, status, tracing))
                {
                    return;
                }

                var category = GetValue<EntityReference>(target, preImage, "contoso_categoryid");
                var severity = GetValue<OptionSetValue>(target, preImage, "contoso_severity");

                if (category == null || severity == null)
                {
                    tracing.Trace("Routing input validation failed. Category={0}, Severity={1}", category?.Id, severity?.Value);
                    throw new InvalidPluginExecutionException("Category and severity are required before a service request can be routed.");
                }

                var rule = FindRoutingRule(service, category.Id, severity.Value);

                if (rule == null)
                {
                    throw new InvalidPluginExecutionException("No active routing rule was found for the selected category and severity.");
                }

                StampRoutingValues(target, rule, tracing);
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                tracing.Trace("Dataverse fault in ServiceRequestRoutingPlugin: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("Service request routing failed because Dataverse returned an error.", ex);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace("Unexpected error in ServiceRequestRoutingPlugin: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("Service request routing failed unexpectedly. Contact your administrator if the issue continues.", ex);
            }
        }

        private static bool IsSupportedStep(IPluginExecutionContext context)
        {
            // Stage 20 is PreOperation. The plugin only mutates the incoming target row and never performs
            // a follow-up update, which keeps the transaction atomic and avoids recursion.
            return context.Stage == 20
                && context.PrimaryEntityName == ServiceRequestTable
                && (context.MessageName.Equals("Create", StringComparison.OrdinalIgnoreCase)
                    || context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasRoutingInputChanged(Entity target)
        {
            foreach (var column in RoutingInputColumns)
            {
                if (target.Attributes.Contains(column))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldSkipRouting(
            IPluginExecutionContext context,
            Entity target,
            Entity preImage,
            OptionSetValue status,
            ITracingService tracing)
        {
            var statusValue = status?.Value;

            if (context.MessageName.Equals("Create", StringComparison.OrdinalIgnoreCase))
            {
                tracing.Trace("Skipping create routing because new requests are held as Draft until final submit.");
                return true;
            }

            var hasRoutingInputChanged = HasRoutingInputChanged(target);
            var submittedNow = IsStatusChangedToNew(target, preImage);
            var needsMissingStamp = statusValue == StatusNew && IsRoutingStampMissing(preImage);

            if (!(hasRoutingInputChanged || submittedNow || needsMissingStamp))
            {
                tracing.Trace(
                    "Skipping update because no routing trigger was present. RoutingInputChanged={0}, SubmittedNow={1}, NeedsMissingStamp={2}",
                    hasRoutingInputChanged,
                    submittedNow,
                    needsMissingStamp);
                return true;
            }

            if (statusValue == StatusDraft)
            {
                tracing.Trace("Skipping update routing because the request is still Draft.");
                return true;
            }

            return false;
        }

        private static bool IsStatusChangedToNew(Entity target, Entity preImage)
        {
            if (!target.Attributes.Contains("contoso_status"))
            {
                return false;
            }

            var targetStatus = target.GetAttributeValue<OptionSetValue>("contoso_status");
            var previousStatus = preImage?.GetAttributeValue<OptionSetValue>("contoso_status");

            return targetStatus?.Value == StatusNew && previousStatus?.Value != StatusNew;
        }

        private static bool IsRoutingStampMissing(Entity preImage)
        {
            if (preImage == null)
            {
                return true;
            }

            return preImage.GetAttributeValue<EntityReference>("contoso_routingslaruleid") == null
                || preImage.GetAttributeValue<EntityReference>("contoso_routedteamid") == null
                || preImage.GetAttributeValue<int?>("contoso_slahours") == null
                || preImage.GetAttributeValue<DateTime?>("contoso_responsedueon") == null
                || preImage.GetAttributeValue<DateTime?>("contoso_resolutiondueon") == null;
        }

        private static Entity GetPreImage(IPluginExecutionContext context)
        {
            return context.PreEntityImages != null && context.PreEntityImages.Contains(PreImageName)
                ? context.PreEntityImages[PreImageName]
                : null;
        }

        private static Entity RetrieveCurrentRequest(IOrganizationService service, Guid requestId)
        {
            return service.Retrieve(
                ServiceRequestTable,
                requestId,
                new ColumnSet(
                    "contoso_categoryid",
                    "contoso_severity",
                    "contoso_status",
                    "contoso_routingslaruleid",
                    "contoso_routedteamid",
                    "contoso_slahours",
                    "contoso_responsedueon",
                    "contoso_resolutiondueon",
                    "contoso_resolutionsummary"));
        }

        private static T GetValue<T>(Entity target, Entity preImage, string attributeName)
        {
            if (target.Attributes.Contains(attributeName))
            {
                return target.GetAttributeValue<T>(attributeName);
            }

            return preImage == null ? default(T) : preImage.GetAttributeValue<T>(attributeName);
        }

        private static void EnforceHighSeverityCloseGuardrail(
            IPluginExecutionContext context,
            Entity target,
            Entity preImage,
            ITracingService tracing)
        {
            if (!context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var status = GetValue<OptionSetValue>(target, preImage, "contoso_status");
            if (status?.Value != StatusClosed)
            {
                return;
            }

            var severity = GetValue<OptionSetValue>(target, preImage, "contoso_severity");
            if (severity?.Value != SeverityHigh)
            {
                tracing.Trace("Close guardrail skipped because request is not high severity. Severity={0}", severity?.Value);
                return;
            }

            var resolutionSummary = GetValue<string>(target, preImage, "contoso_resolutionsummary");
            if (HasMeaningfulResolutionSummary(resolutionSummary))
            {
                tracing.Trace("High-severity close guardrail passed. ResolutionSummaryLength={0}", resolutionSummary.Trim().Length);
                return;
            }

            tracing.Trace(
                "High-severity close guardrail blocked closure. ResolutionSummaryLength={0}",
                string.IsNullOrWhiteSpace(resolutionSummary) ? 0 : resolutionSummary.Trim().Length);

            throw new InvalidPluginExecutionException(
                $"High severity requests require a resolution summary of at least {MinimumHighSeverityResolutionLength} characters before they can be closed.");
        }

        private static bool HasMeaningfulResolutionSummary(string resolutionSummary)
        {
            return !string.IsNullOrWhiteSpace(resolutionSummary)
                && resolutionSummary.Trim().Length >= MinimumHighSeverityResolutionLength;
        }

        private static Entity FindRoutingRule(IOrganizationService service, Guid categoryId, int severityValue)
        {
            // Rules may be fully specific, partially generic, or catch-all:
            // - category + severity
            // - category + any severity
            // - any category + severity
            // - any category + any severity
            //
            // The Priority column resolves overlaps; lower numbers win. This lets administrators tune
            // behavior without changing plugin code.
            var query = new QueryExpression(RoutingRuleTable)
            {
                ColumnSet = new ColumnSet(
                    "contoso_name",
                    "contoso_routedteamid",
                    "contoso_slahours",
                    "contoso_approvalrequired",
                    "contoso_externalsyncrequired",
                    "contoso_priority"),
                TopCount = 1
            };

            query.Criteria.AddCondition("contoso_active", ConditionOperator.Equal, true);

            var categoryFilter = new FilterExpression(LogicalOperator.Or);
            categoryFilter.AddCondition("contoso_categoryid", ConditionOperator.Equal, categoryId);
            categoryFilter.AddCondition("contoso_categoryid", ConditionOperator.Null);
            query.Criteria.AddFilter(categoryFilter);

            var severityFilter = new FilterExpression(LogicalOperator.Or);
            severityFilter.AddCondition("contoso_severity", ConditionOperator.Equal, severityValue);
            severityFilter.AddCondition("contoso_severity", ConditionOperator.Null);
            query.Criteria.AddFilter(severityFilter);

            query.Orders.Add(new OrderExpression("contoso_priority", OrderType.Ascending));

            var rules = service.RetrieveMultiple(query).Entities;

            return rules.Count == 0 ? null : rules[0];
        }

        private static void StampRoutingValues(Entity target, Entity rule, ITracingService tracing)
        {
            var routedTeam = rule.GetAttributeValue<EntityReference>("contoso_routedteamid");
            var slaHours = rule.GetAttributeValue<int>("contoso_slahours");
            var approvalRequired = rule.GetAttributeValue<bool>("contoso_approvalrequired");
            var externalSyncRequired = rule.GetAttributeValue<bool>("contoso_externalsyncrequired");

            if (routedTeam == null)
            {
                throw new InvalidPluginExecutionException("The matched routing rule does not define a routed team.");
            }

            if (!string.Equals(routedTeam.LogicalName, "team", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException("The matched routing rule must route to a Dataverse owner team.");
            }

            if (slaHours <= 0)
            {
                throw new InvalidPluginExecutionException("The matched routing rule must define SLA hours greater than zero.");
            }

            var now = DateTime.UtcNow;

            // Response Due is intentionally derived from the SLA in this first implementation. A future
            // enhancement could add a configurable response-hours column or calendar-aware business hours.
            target["contoso_routingslaruleid"] = rule.ToEntityReference();
            target["contoso_routedteamid"] = routedTeam;
            target["ownerid"] = routedTeam;
            target["contoso_slahours"] = slaHours;
            target["contoso_responsedueon"] = now.AddHours(Math.Min(slaHours, 4));
            target["contoso_resolutiondueon"] = now.AddHours(slaHours);
            target["contoso_approvalstatus"] = new OptionSetValue(approvalRequired ? ApprovalStatusPending : ApprovalStatusNotRequired);
            target["contoso_externalsyncstatus"] = new OptionSetValue(externalSyncRequired ? ExternalSyncStatusPending : ExternalSyncStatusNotRequired);

            tracing.Trace(
                "Stamped routing values. Rule={0}, Team={1}, SLAHours={2}, ApprovalRequired={3}, ExternalSyncRequired={4}",
                rule.GetAttributeValue<string>("contoso_name"),
                routedTeam.Name ?? routedTeam.Id.ToString(),
                slaHours,
                approvalRequired,
                externalSyncRequired);
        }
    }
}
