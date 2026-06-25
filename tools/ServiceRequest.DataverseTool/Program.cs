using Microsoft.Crm.Sdk.Messages;
using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Reflection;
using System.ServiceModel;

const string DefaultEnvironmentUrl = "https://johnbw-dev.crm3.dynamics.com";
const string DefaultClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
const string PublisherUniqueName = "contoso";
const string PublisherDisplayName = "Contoso";
const string PublisherPrefix = "contoso";
const int PublisherOptionValuePrefix = 72700;
const string SolutionUniqueName = "contoso_serviceintake";
const string SolutionDisplayName = "ServiceIntake";
const string SolutionVersion = "1.0.0.0";
const string ServiceRequestTable = "contoso_servicerequest";
const string ServiceCategoryTable = "contoso_servicecategory";
const string ServiceTeamTable = "contoso_serviceteam";
const string RoutingSlaRuleTable = "contoso_routingslarule";
const string ErrorLogTable = "contoso_errorlog";
const string RoutingPluginAssemblyName = "Contoso.ServiceIntake.Plugins";
const string RoutingPluginTypeName = "Contoso.ServiceIntake.Plugins.ServiceRequestRoutingPlugin";
const string RoutingPluginDefaultAssemblyPath = @"src\plugins\Contoso.ServiceIntake.Plugins\bin\Release\net462\Contoso.ServiceIntake.Plugins.dll";

const int SeverityLow = 100000000;
const int SeverityMedium = 100000001;
const int SeverityHigh = 100000002;

const int ApprovalStatusNotRequired = 100000000;
const int ApprovalStatusPending = 100000001;
const int ApprovalStatusApproved = 100000002;
const int ApprovalStatusRejected = 100000003;

const int ExternalSyncStatusNotRequired = 100000000;
const int ExternalSyncStatusPending = 100000001;
const int ExternalSyncStatusSynced = 100000002;
const int ExternalSyncStatusFailed = 100000003;

const int ServiceRequestStatusNew = 100000000;
const int ServiceRequestStatusInProgress = 100000001;
const int ServiceRequestStatusClosed = 100000002;
const int ServiceRequestStatusRejected = 100000003;
const int ServiceRequestStatusDraft = 100000004;

const int ErrorLogSourcePlugin = 100000000;
const int ErrorLogSourceFlow = 100000001;
const int ErrorLogSourcePortal = 100000002;
const int ErrorLogSourceExternalApi = 100000003;

const int ErrorLogStatusNew = 100000000;
const int ErrorLogStatusReviewed = 100000001;
const int ErrorLogStatusResolved = 100000002;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "whoami";
var environmentUrl = GetOption(args, "--url") ?? DefaultEnvironmentUrl;

switch (command)
{
    case "whoami":
        await RunWhoAmIAsync(environmentUrl);
        break;
    case "apply-schema":
        await RunApplySchemaAsync(environmentUrl);
        break;
    case "ensure-draft-status":
        await RunEnsureDraftStatusAsync(environmentUrl);
        break;
    case "seed-routing-data":
        await RunSeedRoutingDataAsync(environmentUrl);
        break;
    case "verify-routing":
        await RunVerifyRoutingAsync(environmentUrl);
        break;
    case "register-plugin":
        await RunRegisterPluginAsync(environmentUrl, GetOption(args, "--assembly") ?? RoutingPluginDefaultAssemblyPath);
        break;
    case "test-plugin-routing":
        await RunTestPluginRoutingAsync(environmentUrl);
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Supported commands: whoami, apply-schema, ensure-draft-status, seed-routing-data, verify-routing, register-plugin, test-plugin-routing");
        return 2;
}

return 0;

static async Task RunWhoAmIAsync(string environmentUrl)
{
    var service = await CreateReadyServiceClientAsync(environmentUrl);
    PrintWhoAmI(service, environmentUrl);
}

static async Task RunApplySchemaAsync(string environmentUrl)
{
    var service = await CreateReadyServiceClientAsync(environmentUrl);
    PrintWhoAmI(service, environmentUrl);

    var publisherId = EnsurePublisher(service);
    EnsureSolution(service, publisherId);

    EnsureServiceCategoryTable(service);
    EnsureServiceRequestTable(service);
    EnsureRoutingSlaRuleTable(service);
    EnsureErrorLogTable(service);
    EnsureColumnDescription(service, ServiceCategoryTable, "contoso_name", "Service category label shown to coordinators and portal users, such as IT, Facilities, HR, or Other.");
    EnsureColumnDescription(service, ServiceRequestTable, "contoso_title", "Short requester-provided summary of the service request.");
    EnsureColumnDescription(service, RoutingSlaRuleTable, "contoso_name", "Editable routing and SLA matrix row evaluated against request category and severity.");
    EnsureColumnDescription(service, ErrorLogTable, "contoso_name", "Short label for an integration, automation, portal, or plugin error event.");

    Publish(service);

    EnsureAttribute(service, ServiceRequestTable, "contoso_confirmationnumber", () =>
        new StringAttributeMetadata
        {
            SchemaName = "contoso_ConfirmationNumber",
            DisplayName = Label("Confirmation Number"),
            Description = Label("System-generated identifier shown to the requester after portal submission and used for follow-up tracking."),
            RequiredLevel = Required(AttributeRequiredLevel.None),
            MaxLength = 100,
            AutoNumberFormat = "SR-{DATETIMEUTC:yyyy}-{SEQNUM:5}"
        });

    EnsureAttribute(service, ServiceRequestTable, "contoso_requesteremail", () =>
        new StringAttributeMetadata
        {
            SchemaName = "contoso_RequesterEmail",
            DisplayName = Label("Requester Email"),
            Description = Label("Email address supplied by the requester for intake confirmations and follow-up messages."),
            RequiredLevel = Required(AttributeRequiredLevel.None),
            MaxLength = 100,
            FormatName = StringFormatName.Email
        });

    EnsureServiceRequestCategoryLookup(service);

    EnsureAttribute(service, ServiceRequestTable, "contoso_severity", () =>
        Picklist("contoso_Severity", "Severity", "Impact level selected for triage and high-severity approval routing.", 100000000, ("Low", 100000000), ("Medium", 100000001), ("High", 100000002)));

    EnsureAttribute(service, ServiceRequestTable, "contoso_description", () =>
        new MemoAttributeMetadata
        {
            SchemaName = "contoso_Description",
            DisplayName = Label("Description"),
            Description = Label("Detailed explanation of the requester issue or need."),
            RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 4000
        });

    EnsureAttribute(service, ServiceRequestTable, "contoso_status", () =>
        Picklist("contoso_Status", "Status", "Current processing state used by coordinators, approvals, and closure validation.", ServiceRequestStatusDraft, ("Draft", ServiceRequestStatusDraft), ("New", ServiceRequestStatusNew), ("In Progress", ServiceRequestStatusInProgress), ("Closed", ServiceRequestStatusClosed), ("Rejected", ServiceRequestStatusRejected)));
    EnsureServiceRequestStatusChoice(service);

    EnsureAttribute(service, ServiceRequestTable, "contoso_resolutionsummary", () =>
        new MemoAttributeMetadata
        {
            SchemaName = "contoso_ResolutionSummary",
            DisplayName = Label("Resolution Summary"),
            Description = Label("Coordinator-entered explanation of how the request was resolved; required with meaningful detail before closing high-severity requests."),
            RequiredLevel = Required(AttributeRequiredLevel.None),
            MaxLength = 4000
        });

    EnsureAttribute(service, ServiceRequestTable, "contoso_submittedvia", () =>
        Picklist("contoso_SubmittedVia", "Submitted Via", "Source channel for the service request, defaulting to Portal for Power Pages submissions.", 100000000, ("Portal", 100000000), ("Internal", 100000001)));

    CleanupLegacyServiceTeamSchema(service);
    EnsureRoutingSlaRuleColumns(service);
    EnsureErrorLogColumns(service);
    EnsureServiceRequestRoutingColumns(service);

    Publish(service);
    AddTablesToSolution(service);
    EnsureCategoryRecords(service);
    EnsureDataverseTeamRecords(service);
    EnsureRoutingSlaRuleRecords(service);
    EnsureTestServiceRequestRecords(service);
    VerifySchema(service);
    VerifyRouting(service);
}

static async Task RunEnsureDraftStatusAsync(string environmentUrl)
{
    var service = await CreateReadyServiceClientAsync(environmentUrl);
    PrintWhoAmI(service, environmentUrl);

    EnsureServiceRequestStatusChoice(service);
    Publish(service);
}

static async Task RunSeedRoutingDataAsync(string environmentUrl)
{
    var service = await CreateReadyServiceClientAsync(environmentUrl);
    PrintWhoAmI(service, environmentUrl);

    EnsureCategoryRecords(service);
    EnsureDataverseTeamRecords(service);
    EnsureRoutingSlaRuleRecords(service);
    EnsureTestServiceRequestRecords(service);
    VerifyRouting(service);
}

static async Task RunVerifyRoutingAsync(string environmentUrl)
{
    var service = await CreateReadyServiceClientAsync(environmentUrl);
    PrintWhoAmI(service, environmentUrl);
    VerifySchema(service);
    VerifyRouting(service);
}

static async Task RunRegisterPluginAsync(string environmentUrl, string assemblyPath)
{
    var service = await CreateReadyServiceClientAsync(environmentUrl);
    PrintWhoAmI(service, environmentUrl);

    var resolvedAssemblyPath = Path.GetFullPath(assemblyPath);
    if (!File.Exists(resolvedAssemblyPath))
    {
        throw new FileNotFoundException("Build the plugin project before registration, or pass --assembly with the compiled DLL path.", resolvedAssemblyPath);
    }

    var pluginAssemblyId = UpsertPluginAssembly(service, resolvedAssemblyPath);
    var pluginTypeId = UpsertPluginType(service, pluginAssemblyId);

    var createStepId = UpsertRoutingPluginStep(
        service,
        pluginTypeId,
        "Create",
        filteringAttributes: null,
        ensurePreImage: false);

    var updateStepId = UpsertRoutingPluginStep(
        service,
        pluginTypeId,
        "Update",
        filteringAttributes: "contoso_categoryid,contoso_severity,contoso_status",
        ensurePreImage: true);

    AddComponentToSolution(service, 91, pluginAssemblyId, "plugin assembly");
    AddComponentToSolution(service, 92, createStepId, "Create plugin step");
    AddComponentToSolution(service, 92, updateStepId, "Update plugin step", includeSubcomponents: true);

    Console.WriteLine("Registered Service Request routing plugin.");
    Console.WriteLine($"Assembly: {RoutingPluginAssemblyName}");
    Console.WriteLine($"Type: {RoutingPluginTypeName}");
    Console.WriteLine("Steps: Create PreOperation synchronous, Update PreOperation synchronous with category/severity/status filtering and PreImage.");
}

static async Task RunTestPluginRoutingAsync(string environmentUrl)
{
    var service = await CreateReadyServiceClientAsync(environmentUrl);
    PrintWhoAmI(service, environmentUrl);

    var title = "TEST - Plugin Routing - IT High";
    var category = RetrieveOne(service, ServiceCategoryTable, "contoso_name", "IT", "contoso_servicecategoryid")
        ?? throw new InvalidOperationException("Missing Service Category seed record: IT");

    var record = new Entity(ServiceRequestTable)
    {
        ["contoso_title"] = title,
        ["contoso_categoryid"] = category.ToEntityReference(),
        ["contoso_severity"] = new OptionSetValue(SeverityHigh),
        ["contoso_requesteremail"] = "plugin.test@contoso.example",
        ["contoso_description"] = "Smoke test request created by the Dataverse SDK tool to verify server-side routing plugin behavior."
    };

    var existing = RetrieveOne(service, ServiceRequestTable, "contoso_title", title, "contoso_servicerequestid");
    Guid requestId;
    if (existing is null)
    {
        requestId = service.Create(record);
        Console.WriteLine($"Created plugin smoke-test request: {title} ({requestId})");
    }
    else
    {
        record.Id = existing.Id;
        service.Update(record);
        requestId = existing.Id;
        Console.WriteLine($"Updated plugin smoke-test request: {title} ({requestId})");
    }

    var request = service.Retrieve(
        ServiceRequestTable,
        requestId,
        new ColumnSet(
            "contoso_title",
            "contoso_categoryid",
            "contoso_severity",
            "contoso_routedteamid",
            "ownerid",
            "contoso_routingslaruleid",
            "contoso_slahours",
            "contoso_responsedueon",
            "contoso_resolutiondueon",
            "contoso_approvalstatus",
            "contoso_externalsyncstatus",
            "contoso_status"));

    var routedTeam = request.GetAttributeValue<EntityReference>("contoso_routedteamid")?.Name;
    var owner = request.GetAttributeValue<EntityReference>("ownerid");
    var rule = request.GetAttributeValue<EntityReference>("contoso_routingslaruleid")?.Name;
    var slaHours = request.GetAttributeValue<int>("contoso_slahours");
    var approval = ApprovalStatusLabel(request.GetAttributeValue<OptionSetValue>("contoso_approvalstatus")?.Value);
    var sync = ExternalSyncStatusLabel(request.GetAttributeValue<OptionSetValue>("contoso_externalsyncstatus")?.Value);
    var status = request.GetAttributeValue<OptionSetValue>("contoso_status")?.Value;

    Console.WriteLine("Plugin Smoke Test Result");
    Console.WriteLine($"- Request: {request.GetAttributeValue<string>("contoso_title")}");
    Console.WriteLine($"- Rule: {rule ?? "(none)"}");
    Console.WriteLine($"- Routed Team: {routedTeam ?? "(none)"}");
    Console.WriteLine($"- Owner: {owner?.Name ?? "(none)"} ({owner?.LogicalName ?? "none"})");
    Console.WriteLine($"- SLA Hours: {slaHours}");
    Console.WriteLine($"- Response Due On: {request.GetAttributeValue<DateTime?>("contoso_responsedueon")}");
    Console.WriteLine($"- Resolution Due On: {request.GetAttributeValue<DateTime?>("contoso_resolutiondueon")}");
    Console.WriteLine($"- Approval Status: {approval}");
    Console.WriteLine($"- External Sync Status: {sync}");
    Console.WriteLine($"- Status Value: {status}");

    if (!string.Equals(routedTeam, "IT Operations", StringComparison.OrdinalIgnoreCase)
        || slaHours != 4
        || !string.Equals(approval, "Pending", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(sync, "Pending", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Plugin smoke test failed. Expected IT High routing to IT Operations, 4-hour SLA, Pending approval, and Pending external sync.");
    }

    Console.WriteLine("Plugin smoke test passed.");
}

static Guid EnsurePublisher(IOrganizationService service)
{
    var existing = RetrieveOne(service, "publisher", "uniquename", PublisherUniqueName, "publisherid", "customizationprefix", "friendlyname");
    if (existing is not null)
    {
        Console.WriteLine($"Publisher exists: {PublisherUniqueName} ({existing.Id})");
        return existing.Id;
    }

    var publisher = new Entity("publisher")
    {
        ["friendlyname"] = PublisherDisplayName,
        ["uniquename"] = PublisherUniqueName,
        ["customizationprefix"] = PublisherPrefix,
        ["customizationoptionvalueprefix"] = PublisherOptionValuePrefix
    };

    var id = service.Create(publisher);
    Console.WriteLine($"Created publisher: {PublisherUniqueName} ({id})");
    return id;
}

static void EnsureSolution(IOrganizationService service, Guid publisherId)
{
    var existing = RetrieveOne(service, "solution", "uniquename", SolutionUniqueName, "solutionid");
    if (existing is not null)
    {
        Console.WriteLine($"Solution exists: {SolutionUniqueName} ({existing.Id})");
        return;
    }

    var solution = new Entity("solution")
    {
        ["friendlyname"] = SolutionDisplayName,
        ["uniquename"] = SolutionUniqueName,
        ["publisherid"] = new EntityReference("publisher", publisherId),
        ["version"] = SolutionVersion
    };

    var id = service.Create(solution);
    Console.WriteLine($"Created solution: {SolutionUniqueName} ({id})");
}

static void EnsureServiceCategoryTable(IOrganizationService service)
{
    if (EntityExists(service, ServiceCategoryTable))
    {
        Console.WriteLine($"Table exists: {ServiceCategoryTable}");
        return;
    }

    var request = new CreateEntityRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        Entity = new EntityMetadata
        {
            SchemaName = "contoso_ServiceCategory",
            DisplayName = Label("Service Category"),
            DisplayCollectionName = Label("Service Categories"),
            Description = Label("Reference values for service request categories."),
            OwnershipType = OwnershipTypes.OrganizationOwned,
            IsActivity = false
        },
        PrimaryAttribute = new StringAttributeMetadata
        {
            SchemaName = "contoso_Name",
            DisplayName = Label("Name"),
            RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 100
        }
    };

    service.Execute(request);
    Console.WriteLine($"Created table: {ServiceCategoryTable}");
}

static void EnsureServiceRequestTable(IOrganizationService service)
{
    if (EntityExists(service, ServiceRequestTable))
    {
        Console.WriteLine($"Table exists: {ServiceRequestTable}");
        return;
    }

    var request = new CreateEntityRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        HasNotes = true,
        Entity = new EntityMetadata
        {
            SchemaName = "contoso_ServiceRequest",
            DisplayName = Label("Service Request"),
            DisplayCollectionName = Label("Service Requests"),
            Description = Label("External service intake request submitted through Power Pages or internal channels."),
            OwnershipType = OwnershipTypes.UserOwned,
            IsActivity = false
        },
        PrimaryAttribute = new StringAttributeMetadata
        {
            SchemaName = "contoso_Title",
            DisplayName = Label("Title"),
            RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 200
        }
    };

    service.Execute(request);
    Console.WriteLine($"Created table: {ServiceRequestTable}");
}

static void EnsureRoutingSlaRuleTable(IOrganizationService service)
{
    EnsureTable(
        service,
        RoutingSlaRuleTable,
        "contoso_RoutingSlaRule",
        "Routing/SLA Rule",
        "Routing/SLA Rules",
        "Editable matrix row that maps request category and severity to routing, SLA, approval, and sync behavior.",
        OwnershipTypes.OrganizationOwned,
        "contoso_Name",
        "Name",
        "Routing/SLA rule name.");
}

static void EnsureErrorLogTable(IOrganizationService service)
{
    EnsureTable(
        service,
        ErrorLogTable,
        "contoso_ErrorLog",
        "Integration/Error Log",
        "Integration/Error Logs",
        "Operational error record written by plugin, flow, portal, or external API handling.",
        OwnershipTypes.OrganizationOwned,
        "contoso_Name",
        "Name",
        "Short error log label.");
}

static void EnsureTable(
    IOrganizationService service,
    string logicalName,
    string schemaName,
    string displayName,
    string collectionDisplayName,
    string description,
    OwnershipTypes ownershipType,
    string primarySchemaName,
    string primaryDisplayName,
    string primaryDescription)
{
    if (EntityExists(service, logicalName))
    {
        Console.WriteLine($"Table exists: {logicalName}");
        return;
    }

    service.Execute(new CreateEntityRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        Entity = new EntityMetadata
        {
            SchemaName = schemaName,
            DisplayName = Label(displayName),
            DisplayCollectionName = Label(collectionDisplayName),
            Description = Label(description),
            OwnershipType = ownershipType,
            IsActivity = false
        },
        PrimaryAttribute = new StringAttributeMetadata
        {
            SchemaName = primarySchemaName,
            DisplayName = Label(primaryDisplayName),
            Description = Label(primaryDescription),
            RequiredLevel = Required(AttributeRequiredLevel.ApplicationRequired),
            MaxLength = 100
        }
    });

    Console.WriteLine($"Created table: {logicalName}");
}

static void EnsureAttribute(IOrganizationService service, string entityName, string logicalName, Func<AttributeMetadata> createAttribute)
{
    var attribute = createAttribute();
    if (AttributeExists(service, entityName, logicalName))
    {
        EnsureColumnDescription(service, entityName, logicalName, LabelText(attribute.Description));
        Console.WriteLine($"Column exists: {entityName}.{logicalName}");
        return;
    }

    service.Execute(new CreateAttributeRequest
    {
        EntityName = entityName,
        Attribute = attribute,
        SolutionUniqueName = SolutionUniqueName
    });

    Console.WriteLine($"Created column: {entityName}.{logicalName}");
}

static void EnsureServiceRequestStatusChoice(IOrganizationService service)
{
    EnsurePicklistOption(service, ServiceRequestTable, "contoso_status", "Draft", ServiceRequestStatusDraft);
    EnsurePicklistDefault(service, ServiceRequestTable, "contoso_status", ServiceRequestStatusDraft);
}

static void EnsurePicklistOption(IOrganizationService service, string entityName, string logicalName, string label, int value)
{
    var response = (RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = entityName,
        LogicalName = logicalName,
        RetrieveAsIfPublished = true
    });

    if (response.AttributeMetadata is not PicklistAttributeMetadata picklist)
    {
        throw new InvalidOperationException($"{entityName}.{logicalName} is not a choice column.");
    }

    var existingOption = picklist.OptionSet.Options.FirstOrDefault(option => option.Value == value);
    if (existingOption is null)
    {
        service.Execute(new InsertOptionValueRequest
        {
            EntityLogicalName = entityName,
            AttributeLogicalName = logicalName,
            Label = Label(label),
            Value = value,
            SolutionUniqueName = SolutionUniqueName
        });

        Console.WriteLine($"Inserted choice option: {entityName}.{logicalName} = {label} ({value})");
        return;
    }

    var existingLabel = LabelText(existingOption.Label);
    if (string.Equals(existingLabel, label, StringComparison.Ordinal))
    {
        Console.WriteLine($"Choice option exists: {entityName}.{logicalName} = {label} ({value})");
        return;
    }

    service.Execute(new UpdateOptionValueRequest
    {
        EntityLogicalName = entityName,
        AttributeLogicalName = logicalName,
        Label = Label(label),
        Value = value,
        SolutionUniqueName = SolutionUniqueName
    });

    Console.WriteLine($"Updated choice option label: {entityName}.{logicalName} = {label} ({value})");
}

static void EnsurePicklistDefault(IOrganizationService service, string entityName, string logicalName, int defaultValue)
{
    var response = (RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = entityName,
        LogicalName = logicalName,
        RetrieveAsIfPublished = true
    });

    if (response.AttributeMetadata is not PicklistAttributeMetadata picklist)
    {
        throw new InvalidOperationException($"{entityName}.{logicalName} is not a choice column.");
    }

    if (picklist.DefaultFormValue == defaultValue)
    {
        Console.WriteLine($"Choice default already set: {entityName}.{logicalName} = {defaultValue}");
        return;
    }

    picklist.DefaultFormValue = defaultValue;
    service.Execute(new UpdateAttributeRequest
    {
        EntityName = entityName,
        Attribute = picklist,
        MergeLabels = true,
        SolutionUniqueName = SolutionUniqueName
    });

    Console.WriteLine($"Updated choice default: {entityName}.{logicalName} = {defaultValue}");
}

static void CleanupLegacyServiceTeamSchema(IOrganizationService service)
{
    DeleteLegacyLookupIfTargetMatches(service, RoutingSlaRuleTable, "contoso_routedteamid", ServiceTeamTable);
    DeleteLegacyLookupIfTargetMatches(service, ServiceRequestTable, "contoso_routedteamid", ServiceTeamTable);

    if (!EntityExists(service, ServiceTeamTable))
    {
        Console.WriteLine($"Legacy table already removed: {ServiceTeamTable}");
        return;
    }

    TryDeleteTable(service, ServiceTeamTable);
}

static void EnsureRoutingSlaRuleColumns(IOrganizationService service)
{
    EnsureLookup(
        service,
        RoutingSlaRuleTable,
        ServiceCategoryTable,
        "contoso_routingslarule_servicecategory",
        "contoso_CategoryId",
        "Category",
        "Optional category match. Leave blank for a category-agnostic fallback rule.",
        AttributeRequiredLevel.None);

    EnsureAttribute(service, RoutingSlaRuleTable, "contoso_severity", () =>
        Picklist("contoso_Severity", "Severity", "Optional severity match. Leave blank for a severity-agnostic fallback rule.", null, ("Low", SeverityLow), ("Medium", SeverityMedium), ("High", SeverityHigh)));

    EnsureLookup(
        service,
        RoutingSlaRuleTable,
        "team",
        "contoso_routingslarule_team",
        "contoso_RoutedTeamId",
        "Routed Team",
        "Dataverse owner team assigned when this routing and SLA rule matches.",
        AttributeRequiredLevel.ApplicationRequired);

    EnsureAttribute(service, RoutingSlaRuleTable, "contoso_slahours", () =>
        WholeNumber("contoso_SlaHours", "SLA Hours", "Resolution target in hours stamped onto matched service requests.", 1, 8760, AttributeRequiredLevel.ApplicationRequired));

    EnsureAttribute(service, RoutingSlaRuleTable, "contoso_approvalrequired", () =>
        Boolean("contoso_ApprovalRequired", "Approval Required", "Whether matching requests require manager approval before external sync or completion.", false));

    EnsureLookup(
        service,
        RoutingSlaRuleTable,
        "systemuser",
        "contoso_routingslarule_approver",
        "contoso_ApproverId",
        "Approver",
        "Internal Dataverse user assigned approval tasks when this routing and SLA rule requires approval.",
        AttributeRequiredLevel.None);

    EnsureAttribute(service, RoutingSlaRuleTable, "contoso_externalsyncrequired", () =>
        Boolean("contoso_ExternalSyncRequired", "External Sync Required", "Whether matching approved requests should be synchronized to the simulated external ERP API.", false));

    EnsureAttribute(service, RoutingSlaRuleTable, "contoso_priority", () =>
        WholeNumber("contoso_Priority", "Priority", "Rule evaluation order. Lower values are evaluated first.", 1, 999999, AttributeRequiredLevel.ApplicationRequired));

    EnsureAttribute(service, RoutingSlaRuleTable, "contoso_active", () =>
        Boolean("contoso_Active", "Active", "Whether this routing and SLA rule participates in matching.", true));

    EnsureAttribute(service, RoutingSlaRuleTable, "contoso_portalmessage", () =>
        Memo("contoso_PortalMessage", "Portal Message", "Requester-facing explanation shown in the portal SLA and routing preview.", AttributeRequiredLevel.None, 2000));
}

static void EnsureErrorLogColumns(IOrganizationService service)
{
    EnsureAttribute(service, ErrorLogTable, "contoso_source", () =>
        Picklist("contoso_Source", "Source", "Component that reported the error.", ErrorLogSourcePlugin, ("Plugin", ErrorLogSourcePlugin), ("Flow", ErrorLogSourceFlow), ("Portal", ErrorLogSourcePortal), ("External API", ErrorLogSourceExternalApi)));

    EnsureAttribute(service, ErrorLogTable, "contoso_operation", () =>
        Text("contoso_Operation", "Operation", "Operation being performed when the error occurred.", 200));

    EnsureLookup(
        service,
        ErrorLogTable,
        ServiceRequestTable,
        "contoso_errorlog_servicerequest",
        "contoso_ServiceRequestId",
        "Service Request",
        "Related service request, when the error is request-specific.",
        AttributeRequiredLevel.None);

    EnsureAttribute(service, ErrorLogTable, "contoso_message", () =>
        Memo("contoso_Message", "Message", "Short error message or business failure summary.", AttributeRequiredLevel.None, 4000));

    EnsureAttribute(service, ErrorLogTable, "contoso_details", () =>
        Memo("contoso_Details", "Details", "Detailed exception, payload, or troubleshooting context.", AttributeRequiredLevel.None, 4000));

    EnsureAttribute(service, ErrorLogTable, "contoso_retryable", () =>
        Boolean("contoso_Retryable", "Retryable", "Whether the failed operation may be safely retried.", false));

    EnsureAttribute(service, ErrorLogTable, "contoso_correlationid", () =>
        Text("contoso_CorrelationId", "Correlation ID", "Correlation identifier shared by related plugin, flow, portal, or API operations.", 100));

    EnsureAttribute(service, ErrorLogTable, "contoso_flowrunid", () =>
        Text("contoso_FlowRunId", "Flow Run ID", "Power Automate flow run identifier for automation failures.", 200));

    EnsureAttribute(service, ErrorLogTable, "contoso_status", () =>
        Picklist("contoso_Status", "Status", "Review status of the logged error.", ErrorLogStatusNew, ("New", ErrorLogStatusNew), ("Reviewed", ErrorLogStatusReviewed), ("Resolved", ErrorLogStatusResolved)));
}

static void EnsureServiceRequestRoutingColumns(IOrganizationService service)
{
    EnsureLookup(
        service,
        ServiceRequestTable,
        "contact",
        "contoso_servicerequest_contact",
        "contoso_RequesterContactId",
        "Requester Contact",
        "Authenticated portal contact that submitted the request; used for contact-scoped portal security.",
        AttributeRequiredLevel.None);

    EnsureLookup(
        service,
        ServiceRequestTable,
        "team",
        "contoso_servicerequest_team",
        "contoso_RoutedTeamId",
        "Routed Team",
        "Dataverse owner team assigned by the matched routing and SLA rule.",
        AttributeRequiredLevel.None);

    EnsureLookup(
        service,
        ServiceRequestTable,
        RoutingSlaRuleTable,
        "contoso_servicerequest_routingslarule",
        "contoso_RoutingSlaRuleId",
        "Routing/SLA Rule",
        "Routing and SLA rule matched when the request was submitted or updated.",
        AttributeRequiredLevel.None);

    EnsureAttribute(service, ServiceRequestTable, "contoso_slahours", () =>
        WholeNumber("contoso_SlaHours", "SLA Hours", "Resolution target in hours copied from the matched routing and SLA rule.", 0, 8760, AttributeRequiredLevel.None));

    EnsureAttribute(service, ServiceRequestTable, "contoso_responsedueon", () =>
        DateTimeColumn("contoso_ResponseDueOn", "Response Due On", "Target date and time for first response."));

    EnsureAttribute(service, ServiceRequestTable, "contoso_resolutiondueon", () =>
        DateTimeColumn("contoso_ResolutionDueOn", "Resolution Due On", "Target date and time for final resolution."));

    EnsureAttribute(service, ServiceRequestTable, "contoso_approvalstatus", () =>
        Picklist("contoso_ApprovalStatus", "Approval Status", "Approval state for routed requests that require manager review.", ApprovalStatusNotRequired, ("Not Required", ApprovalStatusNotRequired), ("Pending", ApprovalStatusPending), ("Approved", ApprovalStatusApproved), ("Rejected", ApprovalStatusRejected)));

    EnsureLookup(
        service,
        ServiceRequestTable,
        "systemuser",
        "contoso_servicerequest_approvedby",
        "contoso_ApprovedBy",
        "Approved By",
        "Internal user who approved the request.",
        AttributeRequiredLevel.None);

    EnsureAttribute(service, ServiceRequestTable, "contoso_approvedon", () =>
        DateTimeColumn("contoso_ApprovedOn", "Approved On", "Date and time when the request was approved."));

    EnsureAttribute(service, ServiceRequestTable, "contoso_externalsystemid", () =>
        Text("contoso_ExternalSystemId", "External System ID", "Identifier returned by the simulated external ERP API after successful sync.", 100));

    EnsureAttribute(service, ServiceRequestTable, "contoso_externalsyncstatus", () =>
        Picklist("contoso_ExternalSyncStatus", "External Sync Status", "External ERP synchronization state.", ExternalSyncStatusNotRequired, ("Not Required", ExternalSyncStatusNotRequired), ("Pending", ExternalSyncStatusPending), ("Synced", ExternalSyncStatusSynced), ("Failed", ExternalSyncStatusFailed)));

    EnsureAttribute(service, ServiceRequestTable, "contoso_externalsyncedon", () =>
        DateTimeColumn("contoso_ExternalSyncedOn", "External Synced On", "Date and time when the request was synchronized to the external ERP system."));

    EnsureAttribute(service, ServiceRequestTable, "contoso_internalresolutionnotes", () =>
        Memo("contoso_InternalResolutionNotes", "Internal Resolution Notes", "Staff-only resolution notes hidden from external portal users.", AttributeRequiredLevel.None, 4000));
}

static void EnsureLookup(
    IOrganizationService service,
    string referencingEntity,
    string referencedEntity,
    string relationshipSchemaName,
    string lookupSchemaName,
    string displayName,
    string description,
    AttributeRequiredLevel requiredLevel)
{
    var logicalName = ToLogicalName(lookupSchemaName);
    if (AttributeExists(service, referencingEntity, logicalName))
    {
        var targets = GetLookupTargets(service, referencingEntity, logicalName);
        if (!targets.Contains(referencedEntity, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{referencingEntity}.{logicalName} exists, but it points to {string.Join(", ", targets)} instead of {referencedEntity}. Remove the legacy lookup dependency and rerun apply-schema.");
        }

        EnsureColumnDescription(service, referencingEntity, logicalName, description);
        Console.WriteLine($"Column exists: {referencingEntity}.{logicalName}");
        return;
    }

    service.Execute(new CreateOneToManyRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        OneToManyRelationship = new OneToManyRelationshipMetadata
        {
            SchemaName = relationshipSchemaName,
            ReferencedEntity = referencedEntity,
            ReferencingEntity = referencingEntity,
            AssociatedMenuConfiguration = new AssociatedMenuConfiguration
            {
                Behavior = AssociatedMenuBehavior.UseCollectionName,
                Group = AssociatedMenuGroup.Details,
                Order = 10000
            },
            CascadeConfiguration = new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade,
                Delete = CascadeType.Restrict,
                Merge = CascadeType.NoCascade,
                Reparent = CascadeType.NoCascade,
                Share = CascadeType.NoCascade,
                Unshare = CascadeType.NoCascade
            }
        },
        Lookup = new LookupAttributeMetadata
        {
            SchemaName = lookupSchemaName,
            DisplayName = Label(displayName),
            RequiredLevel = Required(requiredLevel),
            Description = Label(description)
        }
    });

    Console.WriteLine($"Created lookup column: {referencingEntity}.{logicalName}");
}

static void EnsureServiceRequestCategoryLookup(IOrganizationService service)
{
    if (AttributeExists(service, ServiceRequestTable, "contoso_categoryid"))
    {
        EnsureColumnDescription(service, ServiceRequestTable, "contoso_categoryid", "Category lookup used to route and report on service requests, backed by Contoso service category reference data.");
        Console.WriteLine($"Column exists: {ServiceRequestTable}.contoso_categoryid");
        return;
    }

    service.Execute(new CreateOneToManyRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        OneToManyRelationship = new OneToManyRelationshipMetadata
        {
            SchemaName = "contoso_servicecategory_servicerequest",
            ReferencedEntity = ServiceCategoryTable,
            ReferencingEntity = ServiceRequestTable,
            AssociatedMenuConfiguration = new AssociatedMenuConfiguration
            {
                Behavior = AssociatedMenuBehavior.UseLabel,
                Group = AssociatedMenuGroup.Details,
                Label = Label("Service Requests"),
                Order = 10000
            },
            CascadeConfiguration = new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade,
                Delete = CascadeType.Restrict,
                Merge = CascadeType.NoCascade,
                Reparent = CascadeType.NoCascade,
                Share = CascadeType.NoCascade,
                Unshare = CascadeType.NoCascade
            }
        },
        Lookup = new LookupAttributeMetadata
        {
            SchemaName = "contoso_CategoryId",
            DisplayName = Label("Category"),
            RequiredLevel = Required(AttributeRequiredLevel.None),
            Description = Label("Category lookup used to route and report on service requests, backed by Contoso service category reference data.")
        }
    });

    Console.WriteLine($"Created lookup column: {ServiceRequestTable}.contoso_categoryid");
}

static void EnsureCategoryRecords(IOrganizationService service)
{
    foreach (var category in new[] { "IT", "Facilities", "HR", "Other" })
    {
        var existing = RetrieveOne(service, ServiceCategoryTable, "contoso_name", category, "contoso_servicecategoryid");
        if (existing is not null)
        {
            Console.WriteLine($"Category exists: {category}");
            continue;
        }

        var record = new Entity(ServiceCategoryTable)
        {
            ["contoso_name"] = category
        };

        var id = service.Create(record);
        Console.WriteLine($"Created category: {category} ({id})");
    }
}

static void EnsureDataverseTeamRecords(IOrganizationService service)
{
    var businessUnitId = ((WhoAmIResponse)service.Execute(new WhoAmIRequest())).BusinessUnitId;

    foreach (var team in TeamSeedNames())
    {
        var existing = RetrieveOne(service, "team", "name", team, "teamid", "businessunitid");
        if (existing is not null)
        {
            Console.WriteLine($"Dataverse team exists: {team} ({existing.Id})");
            continue;
        }

        var record = new Entity("team")
        {
            ["name"] = team,
            ["businessunitid"] = new EntityReference("businessunit", businessUnitId),
            ["teamtype"] = new OptionSetValue(0)
        };

        var id = service.Create(record);
        Console.WriteLine($"Created Dataverse owner team: {team} ({id})");
    }
}

static void EnsureRoutingSlaRuleRecords(IOrganizationService service)
{
    var categories = GetRecordsByName(service, ServiceCategoryTable, "contoso_servicecategoryid", "contoso_name");
    var teams = GetRecordsByName(service, "team", "teamid", "name");

    foreach (var rule in new[]
    {
        new RoutingRuleSeed("IT High", "IT", SeverityHigh, "IT Operations", 4, true, true, 10, "This request is expected to route to IT Operations with a 4-hour resolution target. Manager approval is required."),
        new RoutingRuleSeed("IT Medium", "IT", SeverityMedium, "IT Service Desk", 24, false, false, 20, "This request is expected to route to IT Service Desk with a 24-hour resolution target."),
        new RoutingRuleSeed("IT Low", "IT", SeverityLow, "IT Service Desk", 48, false, false, 30, "This request is expected to route to IT Service Desk with a 48-hour resolution target."),
        new RoutingRuleSeed("Facilities High", "Facilities", SeverityHigh, "Facilities Response", 8, true, false, 40, "This request is expected to route to Facilities Response with an 8-hour resolution target. Manager approval is required."),
        new RoutingRuleSeed("Facilities Medium", "Facilities", SeverityMedium, "Facilities Support", 48, false, false, 50, "This request is expected to route to Facilities Support with a 48-hour resolution target."),
        new RoutingRuleSeed("Facilities Low", "Facilities", SeverityLow, "Facilities Support", 48, false, false, 60, "This request is expected to route to Facilities Support with a 48-hour resolution target."),
        new RoutingRuleSeed("HR High", "HR", SeverityHigh, "HR Case Management", 24, true, false, 70, "This request is expected to route to HR Case Management with a 24-hour resolution target. Manager approval is required."),
        new RoutingRuleSeed("HR Medium", "HR", SeverityMedium, "HR Services", 72, false, false, 80, "This request is expected to route to HR Services with a 72-hour resolution target."),
        new RoutingRuleSeed("HR Low", "HR", SeverityLow, "HR Services", 72, false, false, 90, "This request is expected to route to HR Services with a 72-hour resolution target."),
        new RoutingRuleSeed("Catch All", null, null, "General Service Desk", 72, false, false, 999, "This request is expected to route to the General Service Desk with a 72-hour resolution target.")
    })
    {
        if (!teams.TryGetValue(rule.TeamName, out var team))
        {
            throw new InvalidOperationException($"Missing Dataverse team seed record: {rule.TeamName}");
        }

        EntityReference? category = null;
        if (rule.CategoryName is not null)
        {
            if (!categories.TryGetValue(rule.CategoryName, out var categoryRef))
            {
                throw new InvalidOperationException($"Missing service category seed record: {rule.CategoryName}");
            }

            category = categoryRef;
        }

        var existing = RetrieveOne(service, RoutingSlaRuleTable, "contoso_name", rule.Name, "contoso_routingslaruleid");
        var record = new Entity(RoutingSlaRuleTable)
        {
            ["contoso_name"] = rule.Name,
            ["contoso_routedteamid"] = team,
            ["contoso_slahours"] = rule.SlaHours,
            ["contoso_approvalrequired"] = rule.ApprovalRequired,
            ["contoso_externalsyncrequired"] = rule.ExternalSyncRequired,
            ["contoso_priority"] = rule.Priority,
            ["contoso_active"] = true,
            ["contoso_portalmessage"] = rule.PortalMessage
        };

        if (category is not null)
        {
            record["contoso_categoryid"] = category;
        }
        else
        {
            record["contoso_categoryid"] = null;
        }

        if (rule.Severity is not null)
        {
            record["contoso_severity"] = new OptionSetValue(rule.Severity.Value);
        }
        else
        {
            record["contoso_severity"] = null;
        }

        if (existing is null)
        {
            var id = service.Create(record);
            Console.WriteLine($"Created routing/SLA rule: {rule.Name} ({id})");
            continue;
        }

        record.Id = existing.Id;
        service.Update(record);
        Console.WriteLine($"Updated routing/SLA rule: {rule.Name}");
    }
}

static void EnsureTestServiceRequestRecords(IOrganizationService service)
{
    foreach (var request in new[]
    {
        new TestRequestSeed("TEST - IT High Routing", "IT", SeverityHigh, "IT High", "High severity IT request used to verify approval-required routing."),
        new TestRequestSeed("TEST - IT Medium Routing", "IT", SeverityMedium, "IT Medium", "Medium severity IT request used to verify service desk routing."),
        new TestRequestSeed("TEST - Facilities High Routing", "Facilities", SeverityHigh, "Facilities High", "High severity facilities request used to verify response-team routing."),
        new TestRequestSeed("TEST - HR Low Routing", "HR", SeverityLow, "HR Low", "Low severity HR request used to verify HR services routing."),
        new TestRequestSeed("TEST - Other Medium Catch All", "Other", SeverityMedium, "Catch All", "Other category request used to verify default catch-all routing.")
    })
    {
        var category = RetrieveOne(service, ServiceCategoryTable, "contoso_name", request.CategoryName, "contoso_servicecategoryid")
            ?? throw new InvalidOperationException($"Missing category: {request.CategoryName}");
        var rule = RetrieveOne(
            service,
            RoutingSlaRuleTable,
            "contoso_name",
            request.RuleName,
            "contoso_routingslaruleid",
            "contoso_routedteamid",
            "contoso_slahours",
            "contoso_approvalrequired",
            "contoso_externalsyncrequired")
            ?? throw new InvalidOperationException($"Missing routing/SLA rule: {request.RuleName}");

        var slaHours = rule.GetAttributeValue<int>("contoso_slahours");
        var approvalRequired = rule.GetAttributeValue<bool>("contoso_approvalrequired");
        var externalSyncRequired = rule.GetAttributeValue<bool>("contoso_externalsyncrequired");
        var routedTeam = rule.GetAttributeValue<EntityReference>("contoso_routedteamid")
            ?? throw new InvalidOperationException($"Routing/SLA rule is missing routed team: {request.RuleName}");
        var resolutionDueOn = DateTime.UtcNow.AddHours(slaHours);

        var record = new Entity(ServiceRequestTable)
        {
            ["contoso_title"] = request.Title,
            ["contoso_categoryid"] = category.ToEntityReference(),
            ["contoso_severity"] = new OptionSetValue(request.Severity),
            ["contoso_description"] = request.Description,
            ["contoso_status"] = new OptionSetValue(ServiceRequestStatusNew),
            ["contoso_submittedvia"] = new OptionSetValue(100000001),
            ["contoso_routingslaruleid"] = rule.ToEntityReference(),
            ["contoso_routedteamid"] = routedTeam,
            ["contoso_slahours"] = slaHours,
            ["contoso_responsedueon"] = DateTime.UtcNow.AddHours(Math.Max(1, Math.Min(slaHours, 4))),
            ["contoso_resolutiondueon"] = resolutionDueOn,
            ["contoso_approvalstatus"] = new OptionSetValue(approvalRequired ? ApprovalStatusPending : ApprovalStatusNotRequired),
            ["contoso_externalsyncstatus"] = new OptionSetValue(externalSyncRequired ? ExternalSyncStatusPending : ExternalSyncStatusNotRequired),
            ["contoso_requesteremail"] = "test.requester@contoso.example"
        };

        var existing = RetrieveOne(service, ServiceRequestTable, "contoso_title", request.Title, "contoso_servicerequestid");
        if (existing is null)
        {
            var id = service.Create(record);
            Console.WriteLine($"Created test service request: {request.Title} ({id})");
            continue;
        }

        record.Id = existing.Id;
        service.Update(record);
        Console.WriteLine($"Updated test service request: {request.Title}");
    }
}

static void AddTablesToSolution(IOrganizationService service)
{
    AddEntityToSolution(service, ServiceCategoryTable);
    AddEntityToSolution(service, ServiceRequestTable);
    AddEntityToSolution(service, RoutingSlaRuleTable);
    AddEntityToSolution(service, ErrorLogTable);
}

static void AddEntityToSolution(IOrganizationService service, string entityName)
{
    var metadata = RetrieveEntity(service, entityName, EntityFilters.Entity);
    AddComponentToSolution(service, 1, metadata.MetadataId!.Value, $"table {entityName}", includeRequiredComponents: true, includeSubcomponents: true);

    Console.WriteLine($"Added/confirmed table in solution: {entityName}");
}

static Guid UpsertPluginAssembly(IOrganizationService service, string assemblyPath)
{
    var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
    var publicKeyToken = ToPublicKeyTokenString(assemblyName.GetPublicKeyToken());
    var content = Convert.ToBase64String(File.ReadAllBytes(assemblyPath));

    var record = new Entity("pluginassembly")
    {
        ["name"] = RoutingPluginAssemblyName,
        ["content"] = content,
        ["culture"] = string.IsNullOrWhiteSpace(assemblyName.CultureName) ? "neutral" : assemblyName.CultureName,
        ["version"] = assemblyName.Version?.ToString() ?? "1.0.0.0",
        ["publickeytoken"] = publicKeyToken,
        ["isolationmode"] = new OptionSetValue(2), // Sandbox: required for Dataverse online.
        ["sourcetype"] = new OptionSetValue(0), // Database: stores the assembly in Dataverse and solution exports.
        ["description"] = "Contoso Service Intake plugin assembly for server-side routing and SLA stamping."
    };

    var existing = RetrieveOne(service, "pluginassembly", "name", RoutingPluginAssemblyName, "pluginassemblyid");
    if (existing is null)
    {
        var id = service.Create(record);
        Console.WriteLine($"Created plugin assembly: {RoutingPluginAssemblyName} ({id})");
        return id;
    }

    record.Id = existing.Id;
    service.Update(record);
    Console.WriteLine($"Updated plugin assembly: {RoutingPluginAssemblyName} ({existing.Id})");
    return existing.Id;
}

static Guid UpsertPluginType(IOrganizationService service, Guid pluginAssemblyId)
{
    var record = new Entity("plugintype")
    {
        ["pluginassemblyid"] = new EntityReference("pluginassembly", pluginAssemblyId),
        ["typename"] = RoutingPluginTypeName,
        ["name"] = RoutingPluginTypeName,
        ["friendlyname"] = "Service Request Routing Plugin",
        ["description"] = "Routes Contoso Service Request rows by evaluating the editable Routing/SLA Rule matrix."
    };

    var existing = RetrievePluginType(service, pluginAssemblyId);
    if (existing is null)
    {
        var id = service.Create(record);
        Console.WriteLine($"Created plugin type: {RoutingPluginTypeName} ({id})");
        return id;
    }

    record.Id = existing.Id;
    service.Update(record);
    Console.WriteLine($"Updated plugin type: {RoutingPluginTypeName} ({existing.Id})");
    return existing.Id;
}

static Entity? RetrievePluginType(IOrganizationService service, Guid pluginAssemblyId)
{
    var query = new QueryExpression("plugintype")
    {
        ColumnSet = new ColumnSet("plugintypeid"),
        TopCount = 1
    };

    query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, pluginAssemblyId);
    query.Criteria.AddCondition("typename", ConditionOperator.Equal, RoutingPluginTypeName);

    return service.RetrieveMultiple(query).Entities.FirstOrDefault();
}

static Guid UpsertRoutingPluginStep(
    IOrganizationService service,
    Guid pluginTypeId,
    string messageName,
    string? filteringAttributes,
    bool ensurePreImage)
{
    var sdkMessage = RetrieveSdkMessage(service, messageName);
    var sdkMessageFilter = RetrieveSdkMessageFilter(service, sdkMessage.Id, ServiceRequestTable);
    var stepName = $"Contoso Service Request Routing - {messageName}";

    var step = new Entity("sdkmessageprocessingstep")
    {
        ["name"] = stepName,
        ["description"] = $"Runs Contoso Service Intake routing/SLA logic before Service Request {messageName}.",
        ["eventhandler"] = new EntityReference("plugintype", pluginTypeId),
        ["sdkmessageid"] = sdkMessage.ToEntityReference(),
        ["sdkmessagefilterid"] = sdkMessageFilter.ToEntityReference(),
        ["stage"] = new OptionSetValue(20), // PreOperation.
        ["mode"] = new OptionSetValue(0), // Synchronous.
        ["rank"] = 1,
        ["supporteddeployment"] = new OptionSetValue(0), // Server only.
        ["invocationsource"] = new OptionSetValue(0)
    };

    if (!string.IsNullOrWhiteSpace(filteringAttributes))
    {
        step["filteringattributes"] = filteringAttributes;
    }

    var existing = RetrievePluginStep(service, stepName);
    Guid stepId;
    if (existing is null)
    {
        stepId = service.Create(step);
        Console.WriteLine($"Created plugin step: {stepName} ({stepId})");
    }
    else
    {
        step.Id = existing.Id;
        service.Update(step);
        stepId = existing.Id;
        Console.WriteLine($"Updated plugin step: {stepName} ({stepId})");
    }

    if (ensurePreImage)
    {
        // Step images are subcomponents of SDK message processing steps. They are included in the
        // solution by adding the owning step with subcomponents enabled.
        UpsertPreImage(service, stepId);
    }

    return stepId;
}

static Entity RetrieveSdkMessage(IOrganizationService service, string messageName)
{
    var query = new QueryExpression("sdkmessage")
    {
        ColumnSet = new ColumnSet("sdkmessageid", "name"),
        TopCount = 1
    };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, messageName);

    return service.RetrieveMultiple(query).Entities.FirstOrDefault()
        ?? throw new InvalidOperationException($"Could not find SDK message: {messageName}");
}

static Entity RetrieveSdkMessageFilter(IOrganizationService service, Guid sdkMessageId, string primaryObjectTypeCode)
{
    var query = new QueryExpression("sdkmessagefilter")
    {
        ColumnSet = new ColumnSet("sdkmessagefilterid", "primaryobjecttypecode"),
        TopCount = 1
    };
    query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, sdkMessageId);
    query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, primaryObjectTypeCode);
    query.Criteria.AddCondition("iscustomprocessingstepallowed", ConditionOperator.Equal, true);

    return service.RetrieveMultiple(query).Entities.FirstOrDefault()
        ?? throw new InvalidOperationException($"Could not find SDK message filter for {primaryObjectTypeCode}.");
}

static Entity? RetrievePluginStep(IOrganizationService service, string stepName)
{
    var query = new QueryExpression("sdkmessageprocessingstep")
    {
        ColumnSet = new ColumnSet("sdkmessageprocessingstepid"),
        TopCount = 1
    };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, stepName);

    return service.RetrieveMultiple(query).Entities.FirstOrDefault();
}

static Guid UpsertPreImage(IOrganizationService service, Guid stepId)
{
    var imageName = "PreImage";
    var image = new Entity("sdkmessageprocessingstepimage")
    {
        ["name"] = imageName,
        ["entityalias"] = imageName,
        ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId),
        ["imagetype"] = new OptionSetValue(0), // PreImage.
        ["messagepropertyname"] = "Target",
        ["attributes"] = "contoso_categoryid,contoso_severity"
    };

    var existing = RetrievePluginStepImage(service, stepId, imageName);
    if (existing is null)
    {
        var id = service.Create(image);
        Console.WriteLine($"Created plugin step image: {imageName} ({id})");
        return id;
    }

    image.Id = existing.Id;
    service.Update(image);
    Console.WriteLine($"Updated plugin step image: {imageName} ({existing.Id})");
    return existing.Id;
}

static Entity? RetrievePluginStepImage(IOrganizationService service, Guid stepId, string imageAlias)
{
    var query = new QueryExpression("sdkmessageprocessingstepimage")
    {
        ColumnSet = new ColumnSet("sdkmessageprocessingstepimageid"),
        TopCount = 1
    };
    query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId);
    query.Criteria.AddCondition("entityalias", ConditionOperator.Equal, imageAlias);

    return service.RetrieveMultiple(query).Entities.FirstOrDefault();
}

static void AddComponentToSolution(
    IOrganizationService service,
    int componentType,
    Guid componentId,
    string description,
    bool includeRequiredComponents = false,
    bool includeSubcomponents = false)
{
    service.Execute(new AddSolutionComponentRequest
    {
        ComponentType = componentType,
        ComponentId = componentId,
        SolutionUniqueName = SolutionUniqueName,
        AddRequiredComponents = includeRequiredComponents,
        DoNotIncludeSubcomponents = componentType == 1 && !includeSubcomponents
    });

    Console.WriteLine($"Added/confirmed {description} in solution: {componentId}");
}

static void VerifySchema(IOrganizationService service)
{
    var solution = RetrieveOne(service, "solution", "uniquename", SolutionUniqueName, "solutionid");
    var category = RetrieveEntity(service, ServiceCategoryTable, EntityFilters.Entity | EntityFilters.Attributes);
    var request = RetrieveEntity(service, ServiceRequestTable, EntityFilters.Entity | EntityFilters.Attributes | EntityFilters.Relationships);
    var rule = RetrieveEntity(service, RoutingSlaRuleTable, EntityFilters.Entity | EntityFilters.Attributes);
    var errorLog = RetrieveEntity(service, ErrorLogTable, EntityFilters.Entity | EntityFilters.Attributes);

    Console.WriteLine("Verification");
    Console.WriteLine($"Solution: {SolutionUniqueName} ({solution?.Id})");
    Console.WriteLine($"Table: {category.LogicalName}, Ownership={category.OwnershipType}");
    Console.WriteLine($"Table: {request.LogicalName}, Ownership={request.OwnershipType}, HasNotes={request.HasNotes}");
    Console.WriteLine($"Table: {rule.LogicalName}, Ownership={rule.OwnershipType}");
    Console.WriteLine($"Table: {errorLog.LogicalName}, Ownership={errorLog.OwnershipType}");

    foreach (var logicalName in new[]
    {
        "contoso_title",
        "contoso_confirmationnumber",
        "contoso_requesteremail",
        "contoso_categoryid",
        "contoso_severity",
        "contoso_description",
        "contoso_status",
        "contoso_resolutionsummary",
        "contoso_submittedvia",
        "contoso_requestercontactid",
        "contoso_routedteamid",
        "contoso_routingslaruleid",
        "contoso_slahours",
        "contoso_responsedueon",
        "contoso_resolutiondueon",
        "contoso_approvalstatus",
        "contoso_approvedby",
        "contoso_approvedon",
        "contoso_externalsystemid",
        "contoso_externalsyncstatus",
        "contoso_externalsyncedon",
        "contoso_internalresolutionnotes"
    })
    {
        var attribute = request.Attributes.FirstOrDefault(a => a.LogicalName == logicalName);
        Console.WriteLine($"Column: {logicalName}, Type={attribute?.AttributeType}, Required={attribute?.RequiredLevel?.Value}");
    }

    var categories = service.RetrieveMultiple(new QueryExpression(ServiceCategoryTable)
    {
        ColumnSet = new ColumnSet("contoso_name")
    }).Entities.Select(e => e.GetAttributeValue<string>("contoso_name")).OrderBy(v => v);

    Console.WriteLine($"Categories: {string.Join(", ", categories)}");
}

static void VerifyRouting(IOrganizationService service)
{
    Console.WriteLine("Routing/SLA Verification");

    var teams = service.RetrieveMultiple(new QueryExpression("team")
    {
        ColumnSet = new ColumnSet("name", "teamtype"),
        Orders = { new OrderExpression("name", OrderType.Ascending) }
    }).Entities;
    var seededTeamNames = new HashSet<string>(TeamSeedNames(), StringComparer.OrdinalIgnoreCase);

    Console.WriteLine("Dataverse Owner Teams");
    foreach (var team in teams.Where(team => seededTeamNames.Contains(team.GetAttributeValue<string>("name"))))
    {
        Console.WriteLine($"- {team.GetAttributeValue<string>("name")} (Team Type={team.GetAttributeValue<OptionSetValue>("teamtype")?.Value})");
    }

    var ruleQuery = new QueryExpression(RoutingSlaRuleTable)
    {
        ColumnSet = new ColumnSet("contoso_name", "contoso_categoryid", "contoso_severity", "contoso_routedteamid", "contoso_slahours", "contoso_approvalrequired", "contoso_approverid", "contoso_externalsyncrequired", "contoso_priority", "contoso_active"),
        Orders = { new OrderExpression("contoso_priority", OrderType.Ascending) }
    };
    ruleQuery.Criteria.AddCondition("contoso_active", ConditionOperator.Equal, true);
    var rules = service.RetrieveMultiple(ruleQuery).Entities;

    Console.WriteLine("Active Routing/SLA Rules");
    foreach (var rule in rules)
    {
        var category = rule.GetAttributeValue<EntityReference>("contoso_categoryid")?.Name ?? "Any";
        var severity = SeverityLabel(rule.GetAttributeValue<OptionSetValue>("contoso_severity")?.Value);
        var routedTeam = rule.GetAttributeValue<EntityReference>("contoso_routedteamid")?.Name ?? "(missing team)";
        var approver = rule.GetAttributeValue<EntityReference>("contoso_approverid")?.Name ?? "(none)";
        Console.WriteLine($"- {rule.GetAttributeValue<int>("contoso_priority"),3}: {rule.GetAttributeValue<string>("contoso_name")} | Category={category}, Severity={severity}, Team={routedTeam}, SLA={rule.GetAttributeValue<int>("contoso_slahours")}h, Approval={rule.GetAttributeValue<bool>("contoso_approvalrequired")}, Approver={approver}, Sync={rule.GetAttributeValue<bool>("contoso_externalsyncrequired")}");
    }

    var requestQuery = new QueryExpression(ServiceRequestTable)
    {
        ColumnSet = new ColumnSet("contoso_title", "contoso_categoryid", "contoso_severity", "contoso_routedteamid", "contoso_slahours", "contoso_approvalstatus", "contoso_externalsyncstatus"),
        Orders = { new OrderExpression("contoso_title", OrderType.Ascending) }
    };
    requestQuery.Criteria.AddCondition("contoso_title", ConditionOperator.Like, "TEST - %");
    var requests = service.RetrieveMultiple(requestQuery).Entities;

    Console.WriteLine("Test Service Requests");
    foreach (var request in requests)
    {
        var category = request.GetAttributeValue<EntityReference>("contoso_categoryid")?.Name ?? "(none)";
        var severity = SeverityLabel(request.GetAttributeValue<OptionSetValue>("contoso_severity")?.Value);
        var routedTeam = request.GetAttributeValue<EntityReference>("contoso_routedteamid")?.Name ?? "(none)";
        var approval = ApprovalStatusLabel(request.GetAttributeValue<OptionSetValue>("contoso_approvalstatus")?.Value);
        var sync = ExternalSyncStatusLabel(request.GetAttributeValue<OptionSetValue>("contoso_externalsyncstatus")?.Value);
        Console.WriteLine($"- {request.GetAttributeValue<string>("contoso_title")} | Category={category}, Severity={severity}, Team={routedTeam}, SLA={request.GetAttributeValue<int>("contoso_slahours")}h, Approval={approval}, Sync={sync}");
    }
}

static bool EntityExists(IOrganizationService service, string entityName)
{
    try
    {
        _ = RetrieveEntity(service, entityName, EntityFilters.Entity);
        return true;
    }
    catch (FaultException<OrganizationServiceFault> ex) when (ex.Detail.ErrorCode == -2147220969 || ex.Detail.ErrorCode == -2147217150)
    {
        return false;
    }
}

static bool AttributeExists(IOrganizationService service, string entityName, string logicalName)
{
    try
    {
        var metadata = RetrieveEntity(service, entityName, EntityFilters.Attributes);
        return metadata.Attributes.Any(attribute => attribute.LogicalName == logicalName);
    }
    catch (FaultException<OrganizationServiceFault> ex) when (ex.Detail.ErrorCode == -2147220969 || ex.Detail.ErrorCode == -2147217150)
    {
        return false;
    }
}

static string[] GetLookupTargets(IOrganizationService service, string entityName, string logicalName)
{
    var response = (RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = entityName,
        LogicalName = logicalName,
        RetrieveAsIfPublished = true
    });

    return response.AttributeMetadata is LookupAttributeMetadata lookup
        ? lookup.Targets
        : Array.Empty<string>();
}

static void DeleteLegacyLookupIfTargetMatches(IOrganizationService service, string entityName, string logicalName, string legacyTarget)
{
    if (!AttributeExists(service, entityName, logicalName))
    {
        Console.WriteLine($"Legacy lookup already removed: {entityName}.{logicalName}");
        return;
    }

    var targets = GetLookupTargets(service, entityName, logicalName);
    if (!targets.Contains(legacyTarget, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Lookup retained: {entityName}.{logicalName} targets {string.Join(", ", targets)}");
        return;
    }

    try
    {
        service.Execute(new DeleteAttributeRequest
        {
            EntityLogicalName = entityName,
            LogicalName = logicalName
        });
        Console.WriteLine($"Deleted legacy lookup: {entityName}.{logicalName}");
    }
    catch (FaultException<OrganizationServiceFault> ex)
    {
        Console.WriteLine($"Could not delete legacy lookup {entityName}.{logicalName}: {ex.Detail.Message}");
    }
}

static void TryDeleteTable(IOrganizationService service, string entityName)
{
    try
    {
        service.Execute(new DeleteEntityRequest
        {
            LogicalName = entityName
        });
        Console.WriteLine($"Deleted legacy table: {entityName}");
    }
    catch (FaultException<OrganizationServiceFault> ex)
    {
        Console.WriteLine($"Could not delete legacy table {entityName}: {ex.Detail.Message}");
    }
}

static EntityMetadata RetrieveEntity(IOrganizationService service, string entityName, EntityFilters filters)
{
    var response = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
    {
        LogicalName = entityName,
        EntityFilters = filters,
        RetrieveAsIfPublished = true
    });

    return response.EntityMetadata;
}

static Entity? RetrieveOne(IOrganizationService service, string table, string column, object value, params string[] columns)
{
    var query = new QueryExpression(table)
    {
        ColumnSet = new ColumnSet(columns),
        TopCount = 1
    };
    query.Criteria.AddCondition(column, ConditionOperator.Equal, value);

    return service.RetrieveMultiple(query).Entities.FirstOrDefault();
}

static void EnsureColumnDescription(IOrganizationService service, string entityName, string logicalName, string? description)
{
    if (string.IsNullOrWhiteSpace(description))
    {
        return;
    }

    var response = (RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = entityName,
        LogicalName = logicalName,
        RetrieveAsIfPublished = true
    });

    var existing = response.AttributeMetadata;
    var currentDescription = existing.Description?.UserLocalizedLabel?.Label;
    if (string.Equals(currentDescription, description, StringComparison.Ordinal))
    {
        return;
    }

    existing.Description = Label(description);
    service.Execute(new UpdateAttributeRequest
    {
        EntityName = entityName,
        Attribute = existing,
        MergeLabels = true,
        SolutionUniqueName = SolutionUniqueName
    });

    Console.WriteLine($"Updated description: {entityName}.{logicalName}");
}

static PicklistAttributeMetadata Picklist(string schemaName, string displayName, string description, int? defaultValue, params (string Label, int Value)[] options)
{
    var metadata = new PicklistAttributeMetadata
    {
        SchemaName = schemaName,
        DisplayName = Label(displayName),
        Description = Label(description),
        RequiredLevel = Required(AttributeRequiredLevel.None),
        DefaultFormValue = defaultValue ?? -1,
        OptionSet = new OptionSetMetadata
        {
            IsGlobal = false,
            OptionSetType = OptionSetType.Picklist
        }
    };

    foreach (var option in options)
    {
        metadata.OptionSet.Options.Add(new OptionMetadata(Label(option.Label), option.Value));
    }

    return metadata;
}

static StringAttributeMetadata Text(string schemaName, string displayName, string description, int maxLength, AttributeRequiredLevel requiredLevel = AttributeRequiredLevel.None)
{
    return new StringAttributeMetadata
    {
        SchemaName = schemaName,
        DisplayName = Label(displayName),
        Description = Label(description),
        RequiredLevel = Required(requiredLevel),
        MaxLength = maxLength
    };
}

static MemoAttributeMetadata Memo(string schemaName, string displayName, string description, AttributeRequiredLevel requiredLevel, int maxLength)
{
    return new MemoAttributeMetadata
    {
        SchemaName = schemaName,
        DisplayName = Label(displayName),
        Description = Label(description),
        RequiredLevel = Required(requiredLevel),
        MaxLength = maxLength
    };
}

static IntegerAttributeMetadata WholeNumber(string schemaName, string displayName, string description, int minValue, int maxValue, AttributeRequiredLevel requiredLevel)
{
    return new IntegerAttributeMetadata
    {
        SchemaName = schemaName,
        DisplayName = Label(displayName),
        Description = Label(description),
        RequiredLevel = Required(requiredLevel),
        MinValue = minValue,
        MaxValue = maxValue,
        Format = IntegerFormat.None
    };
}

static DateTimeAttributeMetadata DateTimeColumn(string schemaName, string displayName, string description)
{
    return new DateTimeAttributeMetadata
    {
        SchemaName = schemaName,
        DisplayName = Label(displayName),
        Description = Label(description),
        RequiredLevel = Required(AttributeRequiredLevel.None),
        Format = DateTimeFormat.DateAndTime
    };
}

static BooleanAttributeMetadata Boolean(string schemaName, string displayName, string description, bool defaultValue)
{
    return new BooleanAttributeMetadata
    {
        SchemaName = schemaName,
        DisplayName = Label(displayName),
        Description = Label(description),
        RequiredLevel = Required(AttributeRequiredLevel.None),
        DefaultValue = defaultValue,
        OptionSet = new BooleanOptionSetMetadata(
            new OptionMetadata(Label("Yes"), 1),
            new OptionMetadata(Label("No"), 0))
    };
}

static void Publish(IOrganizationService service)
{
    service.Execute(new PublishXmlRequest { ParameterXml = $"<importexportxml><entities><entity>{ServiceCategoryTable}</entity><entity>{ServiceRequestTable}</entity><entity>{RoutingSlaRuleTable}</entity><entity>{ErrorLogTable}</entity></entities></importexportxml>" });
    Console.WriteLine("Published Contoso schema metadata.");
}

static async Task<ServiceClient> CreateReadyServiceClientAsync(string environmentUrl)
{
    var service = await CreateServiceClientAsync(environmentUrl);

    if (!service.IsReady)
    {
        Console.Error.WriteLine(service.LastError);
        if (service.LastException is not null)
        {
            Console.Error.WriteLine(service.LastException);
        }

        throw new InvalidOperationException("Dataverse ServiceClient is not ready.");
    }

    return service;
}

static void PrintWhoAmI(IOrganizationService service, string environmentUrl)
{
    var who = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
    var orgResult = service.RetrieveMultiple(new QueryExpression("organization")
    {
        ColumnSet = new ColumnSet("name", "organizationid"),
        TopCount = 1
    });
    var org = orgResult.Entities.FirstOrDefault();
    var version = (RetrieveVersionResponse)service.Execute(new RetrieveVersionRequest());

    Console.WriteLine("Dataverse SDK authentication succeeded.");
    Console.WriteLine($"Environment: {environmentUrl}");
    Console.WriteLine($"UserId: {who.UserId}");

    if (org is not null)
    {
        Console.WriteLine($"Organization: {org.GetAttributeValue<string>("name")}");
        Console.WriteLine($"OrganizationId: {org.Id}");
    }

    Console.WriteLine($"Version: {version.Version}");
}

static async Task<ServiceClient> CreateServiceClientAsync(string environmentUrl)
{
    var instanceUri = new Uri(environmentUrl.TrimEnd('/'));
    var app = PublicClientApplicationBuilder
        .Create(DefaultClientId)
        .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
        .WithDefaultRedirectUri()
        .Build();

    var scopes = new[] { $"{instanceUri}/.default" };

    async Task<string> AcquireTokenAsync(string _)
    {
        var accounts = await app.GetAccountsAsync();
        try
        {
            var result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                .ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            var result = await app.AcquireTokenInteractive(scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync();

            return result.AccessToken;
        }
    }

    return new ServiceClient(instanceUri, AcquireTokenAsync, useUniqueInstance: true);
}

static Label Label(string text)
{
    return new Label(text, 1033);
}

static string? LabelText(Label? label)
{
    return label?.UserLocalizedLabel?.Label ?? label?.LocalizedLabels?.FirstOrDefault()?.Label;
}

static AttributeRequiredLevelManagedProperty Required(AttributeRequiredLevel level)
{
    return new AttributeRequiredLevelManagedProperty(level);
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string ToPublicKeyTokenString(byte[]? publicKeyToken)
{
    if (publicKeyToken is null || publicKeyToken.Length == 0)
    {
        return string.Empty;
    }

    return BitConverter.ToString(publicKeyToken).Replace("-", string.Empty).ToLowerInvariant();
}

static Dictionary<string, EntityReference> GetRecordsByName(IOrganizationService service, string tableName, string idColumnName, string nameColumnName)
{
    var query = new QueryExpression(tableName)
    {
        ColumnSet = new ColumnSet(idColumnName, nameColumnName)
    };

    return service.RetrieveMultiple(query).Entities
        .Where(entity => !string.IsNullOrWhiteSpace(entity.GetAttributeValue<string>(nameColumnName)))
        .ToDictionary(
            entity => entity.GetAttributeValue<string>(nameColumnName),
            entity => entity.ToEntityReference(),
            StringComparer.OrdinalIgnoreCase);
}

static string[] TeamSeedNames()
{
    return
    [
        "IT Operations",
        "IT Service Desk",
        "Facilities Response",
        "Facilities Support",
        "HR Case Management",
        "HR Services",
        "General Service Desk"
    ];
}

static string ToLogicalName(string schemaName)
{
    return schemaName.ToLowerInvariant();
}

static string SeverityLabel(int? value)
{
    return value switch
    {
        SeverityLow => "Low",
        SeverityMedium => "Medium",
        SeverityHigh => "High",
        null => "Any",
        _ => value.Value.ToString()
    };
}

static string ApprovalStatusLabel(int? value)
{
    return value switch
    {
        ApprovalStatusNotRequired => "Not Required",
        ApprovalStatusPending => "Pending",
        ApprovalStatusApproved => "Approved",
        ApprovalStatusRejected => "Rejected",
        null => "(none)",
        _ => value.Value.ToString()
    };
}

static string ExternalSyncStatusLabel(int? value)
{
    return value switch
    {
        ExternalSyncStatusNotRequired => "Not Required",
        ExternalSyncStatusPending => "Pending",
        ExternalSyncStatusSynced => "Synced",
        ExternalSyncStatusFailed => "Failed",
        null => "(none)",
        _ => value.Value.ToString()
    };
}

sealed record RoutingRuleSeed(
    string Name,
    string? CategoryName,
    int? Severity,
    string TeamName,
    int SlaHours,
    bool ApprovalRequired,
    bool ExternalSyncRequired,
    int Priority,
    string PortalMessage);

sealed record TestRequestSeed(string Title, string CategoryName, int Severity, string RuleName, string Description);
