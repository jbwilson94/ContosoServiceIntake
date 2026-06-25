# Contoso Service Intake

## Overview

Contoso Service Intake is a senior Power Platform take-home solution for authenticated external service request submission, configurable routing/SLA handling, manager approval, simulated ERP synchronization, and internal coordinator triage.

The solution uses Power Pages for the requester experience, Dataverse for the data model and security boundary, Power Automate for approval and external integration orchestration, a C# Dataverse plugin for authoritative server-side rules, and a PCF control for the model-driven coordinator experience.

## Design Desisions

Requirment A. Architecture & Data Strategy (Dataverse)
    • Design a relational data model that supports service requests, a dynamic routing/SLA rules engine, and system error logging.
    • Implement a robust security model ensuring external users only see their own data, and sensitive internal fields (e.g., internal resolution notes) are hidden from unauthorized staff.
    • The system must automatically generate a user-friendly, formatted Confirmation Number forevery request.

Design:
    - Simple data model: Service Request is the main table. Service Category is a lookup table instead of a choice so the values are easier to maintain. Routing/SLA Rule controls the routed team, SLA, approval, approver, and external sync settings. It uses the OOTB Team table for ownership and System User for approvals. Error Log captures flow, plugin, and API issues. Contact is used for authenticated portal users.
    - I kept the security role work simple for the assessment. I added Service Request permissions so routed teams can own requests. In a real environment I would extend this with least-privilege read access to Contact, Service Category, and Routing/SLA Rule as needed.
    - For Power Pages table permissions, everything is based on the authenticated OOTB web role. Service Category has global read, Contact has self access for the profile form, and Service Request uses contact scope so users only see their own requests.
    - Confirmation Number is an autonumber column so every request gets a formatted friendly number automatically.

Requirment B. External User Experience (Power Pages)
    • Build an authenticated portal where users can submit their requests and upload supporting
    documentation.
    • The submission process must be multi-step.
    • Advanced Requirement: The portal must provide real-time dynamic feedback before
    submission (e.g., calculating and displaying the expected SLA or routing destination on the
    screen based on user inputs) without requiring a full page reload. You must utilize the Power
    Pages Web API and/or Liquid templating to achieve this.

Design:
    - Made Entra ID the default login provider and used contact-scoped permissions on Service Request so portal users only see their own records.
    - The multi-step form is split into Request Details first, then Supporting Documents.
    - For the live routing/SLA feedback, category and severity changes call a lightweight `/fetch-routing` page that returns JSON generated with Liquid/FetchXML. I chose this instead of enabling the full Power Pages Web API because it is simpler, and less chance to expose unwanted column data. And plain liquid would not work since this only runs on page refresh.

Requirment C. Process Automation & Integration (Power Automate)
    • Design an automated process to handle approvals for high-priority items.
    • Integration Requirement: Upon approval, the process must simulate syncing the data to an
    external REST API (e.g., posting to a mock endpoint like reqres.in) and storing the external
    system’s ID back in Dataverse.
    • Resiliency: The automation must utilize enterprise error-handling patterns (e.g., Try/Catch
    scopes). Any failures in the API call or approval process must be gracefully handled and written
    to your custom Dataverse error log.

Design:
    - Approval flow starts when the plugin routes a submitted request and sets Approval Status to `Pending`.
    - The flow reads the approver from the Routing/SLA Rule, waits for approval, then posts to a mock REST endpoint and stores the returned external system ID back on the request.
    - The flow uses simple try/catch-style scopes. If approval or external sync fails, the catch path writes the details to the Error Log table with the flow run context.

Requirment D. Extensibility & Pro-Code While low-code is preferred, complex enterprise scenarios require pro-code extensibility. You must implement the following, justifying your architectural placement for each:
    • Backend Logic (C# Plugin or Custom API): Implement a server-side component to handle a
    complex business rule. For example, use a plugin to dynamically route the ticket based on your
    rules engine, or use it to enforce the guardrail that blocks closing a critical request without
    sufficient resolution documentation.
    • Frontend UX (PCF Control): Develop a custom Power Apps Component Framework (PCF) control
    to enhance the model-driven app experience for internal coordinators (e.g., a visual severity
    selector, a custom timeline, or a dynamic status indicator).

Design:
    - C# Plugin handles the core server-side rules for Service Requests. Since the multi-step portal creates the row before the user is fully done, the plugin keeps the request in `Draft` on create. The final portal step changes the status to `New`, and that update is the trigger for routing. The plugin then reads the Routing/SLA Rule table, assigns the correct owner team, sets the SLA fields, and sets the approval and external sync statuses. It also prevents high severity requests from being closed without a resolution summary. This is in a plugin because this logic needs to run the same way from the portal, model-driven app, import, API, or flow.
    - PCF gives internal users a quick summary of how the request is being handled. It shows routing, owner, SLA, approval, external sync, and close readiness in one place. It does not update the record. I used a PCF because these fields could be shown separately on the form, but the control makes the handling status easier to read.


DESIGN NOTE: Due to the limited assessment timeline, I used Codex to help accelerate work such as generating Dataverse tables and columns, Power Pages components, the C# plugin, PCF control and documentation. I still made the architecture and design decisions and reviewed the generated output as part of the build.

## Delivered Components

- Managed Dataverse solution: `contoso_serviceintake`
- Power Pages site export: `portal/site` in the delivery package
- Unpacked solution source: `solutions/contoso_serviceintake`
- C# plugin source: `src/plugins/Contoso.ServiceIntake.Plugins`
- PCF source: `src/pcf/RequestHandlingSummary`
- Dataverse SDK utility: `tools/ServiceRequest.DataverseTool`
- Architecture/implementation notes: `docs`

## Architecture Summary

| Layer | Component | Responsibility |
| --- | --- | --- |
| Data | Dataverse tables | Service requests, categories, routing/SLA rules, and integration error logging |
| Portal | Power Pages | Authenticated request creation, supporting documents, request list, confirmation page, live routing preview |
| Automation | Power Automate | Manager approval, mock ERP synchronization, and automation error logging |
| Server-side code | C# Dataverse plugin | Draft lifecycle, routing/SLA stamping, owner-team assignment, and high-severity close guardrail |
| Internal UX | PCF control | Coordinator-facing action center for routing, SLA, approval, sync, and close-readiness signals |
| ALM | PAC exports | Managed solution plus unpacked source for review and transport |

## Data Model

The primary custom tables are:

- `contoso_servicerequest` - external service request submitted through the portal or internal channels.
- `contoso_servicecategory` - category reference data used by the portal and routing rules.
- `contoso_routingslarule` - editable routing/SLA matrix that maps category/severity to owner team, SLA, approval, approver, and external sync behavior.
- `contoso_errorlog` - operational error log written by plugins and flows.

The solution also uses OOTB Dataverse tables:

- `contact` for authenticated portal users.
- `team` for owner-team assignment.
- `systemuser` for configurable approvers.
- `annotation` for portal-uploaded supporting documentation.

See [docs/erd.md](docs/erd.md) for the ERD.

## Request Lifecycle

1. A signed-in portal user starts the multi-step service request form.
2. The initial row is created as `Draft` so partially completed portal submissions do not enter operational queues.
3. The portal shows live routing feedback by calling a lightweight `/fetch-routing` page backed by Liquid/FetchXML.
4. On final submit, the portal sets `Status = New`.
5. The plugin matches the active `Routing/SLA Rule`, assigns the owner team, stamps SLA fields, and sets approval/external sync statuses.
6. High-severity paths set `Approval Status = Pending` and trigger the approval flow.
7. When approved, the flow calls a mock ERP endpoint and stores the returned external ID.
8. Internal coordinators use the model-driven app and PCF action center to monitor routing, SLA, approval, sync, and close readiness.
9. High-severity requests cannot be closed unless `Resolution Summary` contains at least 20 meaningful characters.

## Power Pages Experience

The portal supports:

- Authenticated requester home page with signed-in and signed-out Liquid branches.
- `Create Service Request` multi-step form.
- Required supporting document upload using notes/timeline configuration.
- `My Requests` list scoped by contact table permission.
- Confirmation page that reads request details from the submitted request id.
- Live routing/SLA preview without a full page reload through `/fetch-routing`.
- Shared styling through `contoso-theme.css` and shared JavaScript through `contoso-common.js`.

External users are intentionally limited to their own request data. Sensitive internal fields such as `contoso_internalresolutionnotes` are not exposed on portal forms or lists.

## Security Strategy

Portal users authenticate through the configured identity provider and map to Dataverse `contact` rows.

Power Pages table permissions are scoped as follows:

- `Service Request`: contact scope, create/read/write for the signed-in user's own requests.
- `Service Category`: global read for lookup values.
- `Routing/SLA Rule`: global read for portal routing preview only.
- `Note`: child permission under Service Request for supporting documents.
- `Contact`: self/profile access for authenticated profile maintenance.

Internal fulfillment uses Dataverse owner teams. The plugin assigns `ownerid` to the routed owner team, and `contoso_routedteamid` preserves the routing decision for reporting and audit.

## Routing/SLA Rule Engine

Routing rules are data driven by `contoso_routingslarule`.

The plugin supports:

- category + severity specific rules
- category fallback rules
- severity fallback rules
- catch-all fallback rules
- priority-based overlap resolution

The plugin stamps:

- `contoso_routingslaruleid`
- `contoso_routedteamid`
- `ownerid`
- `contoso_slahours`
- `contoso_responsedueon`
- `contoso_resolutiondueon`
- `contoso_approvalstatus`
- `contoso_externalsyncstatus`

This logic is implemented in a plugin rather than a flow because it must run consistently and transactionally for portal submits, model-driven app updates, imports, and API writes.

## Approval And ERP Sync

`Service Request - Approval Flow` runs when a Service Request reaches:

- `Status = New`
- `Approval Status = Pending`
- `Routing/SLA Rule` populated

The flow:

1. Retrieves the matched Routing/SLA Rule.
2. Retrieves the configured approver from `contoso_approverid`.
3. Creates and waits for an approval.
4. If approved, stamps `Approval Status = Approved` and `Approved On`.
5. If external sync is pending, posts to the mock ERP endpoint.
6. On sync success, stamps `External Sync Status = Synced`, `External System ID`, and `External Synced On`.
7. On rejection, stamps `Approval Status = Rejected` and `Status = Rejected`.
8. On flow/API failure, writes `contoso_errorlog`.

The mock ERP endpoint is `https://jsonplaceholder.typicode.com/posts`, which returns a simulated external id.

Power Automate is used for this integration because approval waits, HTTP retries, run history, and catch/error scopes are orchestration concerns. The plugin remains responsible for deterministic Dataverse business rules.

## Pro-Code Components

### C# Plugin

Location: `src/plugins/Contoso.ServiceIntake.Plugins`

`ServiceRequestRoutingPlugin` implements:

- draft creation for portal multi-step forms
- submit-time routing/SLA stamping
- owner-team assignment
- approval/sync status initialization
- high-severity close guardrail

See [docs/plugin-delivery.md](docs/plugin-delivery.md).

### PCF Control

Location: `src/pcf/RequestHandlingSummary`

`Contoso Request Action Center` is a read-only model-driven app control that summarizes:

- routing/owner state
- SLA status
- approval status
- external sync status
- response target
- close readiness

The PCF does not mutate Dataverse. It helps coordinators understand the next operational action quickly while the plugin and flows remain the enforcement layers.

See [docs/pcf-request-handling-summary.md](docs/pcf-request-handling-summary.md).

## Validation Notes

Verified implementation paths include:

- plugin routes submitted IT High requests to `IT Operations`
- plugin keeps draft portal rows unrouted until final submit
- plugin blocks high-severity close when Resolution Summary is missing or too short
- approval flow creates an approval assigned from the Routing/SLA Rule approver
- approval flow includes external sync branch and error logging branch
- portal live routing preview reads the same routing matrix used by the plugin

Useful test records created during validation include:

- `SR-2026-01029` - approval creation validation
- `SR-2026-01031` - approval + ERP sync validation candidate
- `SR-2026-01032` - high-severity close guardrail validation

## Build And Export Commands

Build plugin:

```powershell
dotnet build src/plugins/Contoso.ServiceIntake.Plugins/Contoso.ServiceIntake.Plugins.csproj --configuration Release
```

Build PCF:

```powershell
npm run build --prefix src/pcf/RequestHandlingSummary
```

Export managed solution:

```powershell
pac solution export --environment https://johnbw-dev.crm3.dynamics.com --name contoso_serviceintake --path exports/contoso_serviceintake_managed.zip --managed --overwrite
```

Unpack solution source:

```powershell
pac solution unpack --zipfile exports/contoso_serviceintake_unmanaged.zip --folder solutions/contoso_serviceintake --packagetype Unmanaged --allowWrite --clobber
```

Download Power Pages source:

```powershell
pac pages download --environment https://johnbw-dev.crm3.dynamics.com --path powerpages/contoso-service-intake --webSiteId ff5f9ba7-78bd-4c6a-b424-b433e87b41ef --overwrite --modelVersion Enhanced
```

## Package Contents

The final delivery folder contains:

- `managed-solution/contoso_serviceintake_managed.zip`
- `solution-source/contoso_serviceintake`
- `portal/site`
- `src/plugins/Contoso.ServiceIntake.Plugins`
- `src/pcf/RequestHandlingSummary`
- `tools/ServiceRequest.DataverseTool`
- `docs`
- `README.md`

## Known Notes

- The DEV plugin steps currently run as `# JohnAdmin` so owner-team assignment has the required Dataverse privileges. In production, use a dedicated service user with least-privilege roles.
- The approval flow stamps `Approved On`; the approval records themselves retain the approver/owner. The custom `Approved By` lookup can be populated later through the Power Automate designer's native lookup picker if required.
- The mock ERP endpoint is intentionally non-production and returns simulated ids for demonstration.
