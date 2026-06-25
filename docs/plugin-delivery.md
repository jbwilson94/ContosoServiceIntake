# Contoso Service Intake Plugin Delivery

## Purpose

`Contoso.ServiceIntake.Plugins` contains server-side Dataverse logic required by the senior assessment.

The first plugin is `ServiceRequestRoutingPlugin`. It routes a Service Request by reading the editable `contoso_routingslarule` matrix and stamping the matched routing/SLA fields before the request is saved. The same plugin also enforces the high-severity close guardrail so internal users cannot close critical requests without enough resolution documentation.

This satisfies the senior assessment pro-code requirement by placing complex server-side business rules in a C# Dataverse plugin instead of relying only on Power Pages JavaScript or Power Automate. The portal can still show a live preview, and the PCF can still surface close-readiness guidance, but the plugin is the authoritative enforcement layer.

## Source Location

Raw plugin source:

```text
src/plugins/Contoso.ServiceIntake.Plugins
```

This folder should be included in the final source repository package alongside the unpacked Power Platform solution.

## Build

```powershell
dotnet build src/plugins/Contoso.ServiceIntake.Plugins/Contoso.ServiceIntake.Plugins.csproj --configuration Release
```

The project targets `.NET Framework 4.6.2`, uses the Microsoft Power Platform plugin scaffold, and signs the assembly with the generated strong-name key.

Release build output:

```text
src/plugins/Contoso.ServiceIntake.Plugins/bin/Release/net462/Contoso.ServiceIntake.Plugins.dll
```

## Registration

The repository includes a repeatable Dataverse SDK registration command. It creates or updates the plugin assembly, plugin type, Create step, Update step, and Update pre-image, then adds the deployable components to `contoso_serviceintake`.

Build the SDK utility:

```powershell
dotnet build tools/ServiceRequest.DataverseTool/ServiceRequest.DataverseTool.csproj
```

Register the plugin in DEV:

```powershell
dotnet ./tools/ServiceRequest.DataverseTool/bin/Debug/net10.0/ServiceRequest.DataverseTool.dll register-plugin --url https://johnbw-dev.crm3.dynamics.com --assembly src/plugins/Contoso.ServiceIntake.Plugins/bin/Release/net462/Contoso.ServiceIntake.Plugins.dll
```

If using the Plugin Registration Tool or Power Platform Tools manually, use the same assembly and step configuration below.

Assembly:

```text
Contoso.ServiceIntake.Plugins.dll
```

Plugin class:

```text
Contoso.ServiceIntake.Plugins.ServiceRequestRoutingPlugin
```

Use the default Dataverse online registration settings:

- Isolation mode: `Sandbox`
- Location: `Database`

Run-as context:

- Recommended production setting: a dedicated Dataverse service user with the minimum privileges needed to read routing configuration and assign Service Requests to owner teams.
- Current DEV setting: the plugin steps can run as an admin user while testing ownership assignment.
- Do not leave the steps running as the Power Pages/calling user if the plugin sets `ownerid`; portal runtime users commonly do not have the assign privileges required for team ownership changes.

### Step 1: Create

- Message: `Create`
- Primary table: `contoso_servicerequest`
- Event pipeline stage: `PreOperation`
- Execution mode: `Synchronous`
- Filtering attributes: none
- Pre image: none

### Step 2: Update

- Message: `Update`
- Primary table: `contoso_servicerequest`
- Event pipeline stage: `PreOperation`
- Execution mode: `Synchronous`
- Filtering attributes:
  - `contoso_categoryid`
  - `contoso_severity`
  - `contoso_status`
- Pre image name: `PreImage`
- Pre image columns:
  - `contoso_categoryid`
  - `contoso_severity`

## Solution Packaging

After registering the assembly and steps, add these components to the unmanaged solution:

```text
contoso_serviceintake
```

Add:

- Plugin assembly
- Create step
- Update step

This is required because registering a plugin assembly initially adds it to the default solution. The senior assessment deliverable expects the managed solution export to include custom code registrations.

## Design Notes

The routing logic belongs in a plugin instead of client-side JavaScript or Power Automate because it must run consistently for every create/update path: portal, model-driven app, import, API, or automation.

The plugin runs in `PreOperation` so it can stamp fields directly onto the target row without issuing a second update, which avoids recursion and keeps the transaction atomic.

Power Pages multi-step forms create the Dataverse record before the requester reaches the final submit step. Because of that, the plugin defaults new Service Requests to `Draft` and does not assign ownership or start the SLA clock on the initial draft create. The final multi-step form submit step promotes the request to `New`; that status change is the authoritative submit event and the plugin stamps routing, ownership, response due, and resolution due values at that time.

The Update step still recalculates routing when Category or Severity changes after submission. Draft updates remain unrouted so partially completed portal submissions do not enter operational team queues.

On Update, the plugin retrieves the current Service Request row to evaluate status and existing routing/SLA values. The registered pre-image is intentionally minimal and only carries Category and Severity as a compatibility fallback.

The plugin assigns the request to the routed Dataverse owner team by setting `ownerid`. It also stamps `contoso_routedteamid` as an audit/reporting field so the original routing decision remains visible even if ownership is later reassigned manually.

This uses OOTB owner teams because routed service requests are operational work items owned by a queue/team. Access teams are better for ad hoc collaboration, but they do not express primary ownership of the request.

The Routing/SLA Rule table remains the editable configuration point:

- `contoso_categoryid` can be set or left blank for a category fallback.
- `contoso_severity` can be set or left blank for a severity fallback.
- `contoso_priority` resolves overlaps; lower numbers win.
- `contoso_routedteamid`, `contoso_slahours`, approval, approver, and external sync flags drive the stamped request fields and downstream automation.
- `contoso_routedteamid` must reference an OOTB `team` row.
- `contoso_approverid` references the Dataverse user assigned to approve requests when `contoso_approvalrequired` is true. Approval flows should use this configurable lookup before falling back to any environment-level default approver.

The Power Pages live preview uses the same table for user feedback, but the preview is not trusted as final. The plugin recalculates the same decision at save time.

The high-severity close guardrail also runs on Update before routing skip logic. If a user or automation sets `contoso_status` to `Closed` for a high-severity request, the plugin checks `contoso_resolutionsummary`. The close is blocked unless the trimmed resolution summary is at least 20 characters. This keeps the rule effective across model-driven forms, imports, API writes, and flows.

## Stamped Fields

The plugin stamps:

- `contoso_routingslaruleid`
- `contoso_routedteamid`
- `ownerid`
- `contoso_slahours`
- `contoso_responsedueon`
- `contoso_resolutiondueon`
- `contoso_approvalstatus`
- `contoso_externalsyncstatus`
- `contoso_status` to `Draft` on create when no status is supplied

The plugin blocks routed saves when Category or Severity is missing, or when no active Routing/SLA Rule matches the submitted values. Draft creation is allowed to support multi-step portal forms, but submit-to-`New` is blocked unless the request can be routed.

The plugin also blocks high-severity closure when `contoso_resolutionsummary` is missing or too short. The PCF mirrors the same 20-character threshold visually, but the server-side plugin is the actual control.

## Error Handling

The plugin traces execution details to the Dataverse plugin trace log and throws user-friendly `InvalidPluginExecutionException` messages for invalid routing configuration, such as no active rule or a rule without SLA/team values. Close guardrail failures use the same user-friendly exception pattern so agents see a clear remediation message.

## Verification Plan

After registration:

1. Create a new Service Request draft from Power Pages with Category `IT` and Severity `High`.
2. Confirm the draft is not assigned to a routed team and does not have response/resolution due dates.
3. Submit the final multi-step form step so Status becomes `New`.
4. Confirm the submitted request is stamped with:
   - `contoso_routedteamid`: IT Operations
   - `ownerid`: IT Operations owner team
   - `contoso_slahours`: 4
   - `contoso_approvalstatus`: Pending
   - `contoso_externalsyncstatus`: Pending
5. Create and submit a request with Category `Other` and Severity `Medium`.
6. Confirm the catch-all rule routes the request to General Service Desk with a 72-hour SLA.
7. Update an existing submitted request's Category or Severity and confirm the plugin recalculates the routing fields.
8. Update unrelated fields and confirm routing is not recalculated.
9. Attempt to close a high-severity request with a blank or very short Resolution Summary.
10. Confirm the save is blocked with the message `High severity requests require a resolution summary of at least 20 characters before they can be closed.`
11. Add a meaningful Resolution Summary and close the same request successfully.

Automated smoke test command:

```powershell
dotnet ./tools/ServiceRequest.DataverseTool/bin/Debug/net10.0/ServiceRequest.DataverseTool.dll test-plugin-routing --url https://johnbw-dev.crm3.dynamics.com
```

Expected smoke test outcome:

- Request title: `TEST - Plugin Routing - IT High`
- Rule: `IT High`
- Routed Team: `IT Operations`
- Owner: `IT Operations` (`team`)
- SLA Hours: `4`
- Approval Status: `Pending`
- External Sync Status: `Pending`

## Assessment Delivery Checklist

Include these items in the final `Enterprise_ServiceIntake_<YourName>.zip` package:

- Raw plugin source folder: `src/plugins/Contoso.ServiceIntake.Plugins`
- Compiled plugin assembly produced by the Release build
- Managed solution export containing the plugin assembly and both registered steps
- Unpacked solution source
- This delivery note or the final README section describing plugin rationale, registration, and verification
