# Contoso Request Action Center PCF

## Purpose

`Contoso Request Action Center` is an internal model-driven app PCF control for service coordinators.

The senior assessment asks for a PCF that improves the model-driven app experience for internal coordinators. This control solves a practical triage problem: a coordinator should not have to inspect routing, owner, SLA, approval, sync, and resolution fields one by one to know what needs attention.

The control turns those fields into one read-only action recommendation:

- approval required or rejected
- draft portal submissions that should not be worked yet
- external sync pending or failed
- response or resolution SLA overdue
- routed team and owner mismatch
- high-severity close-readiness based on the resolution-summary guardrail
- normal/on-track status when no intervention is needed

## Design

The control is intentionally read-only.

- The C# plugin remains the authoritative layer for routing, ownership, SLA stamping, and close guardrails.
- Power Automate remains the correct layer for approvals and external API sync.
- The PCF uses the model-driven app Web API to read the current row's owner and routed team because the form designer can be unreliable when mapping lookup fields as PCF input properties.
- The PCF does not call external services or mutate Dataverse data.
- The PCF otherwise interprets fields already loaded on the model-driven form and presents the next coordinator action.

This keeps the component useful without moving business enforcement into the client.

## Source Location

```text
src/pcf/RequestHandlingSummary
```

## Bound And Input Fields

Add the control to the internal Service Request main form and map these fields:

| PCF property | Dataverse column |
| --- | --- |
| `status` | `contoso_status` |
| `severity` | `contoso_severity` |
| `slaHours` | `contoso_slahours` |
| `responseDueOn` | `contoso_responsedueon` |
| `resolutionDueOn` | `contoso_resolutiondueon` |
| `approvalStatus` | `contoso_approvalstatus` |
| `externalSyncStatus` | `contoso_externalsyncstatus` |
| `externalSystemId` | `contoso_externalsystemid` |
| `resolutionSummary` | `contoso_resolutionsummary` |

Do not map `ownerid` or `contoso_routedteamid` as PCF input properties. The control retrieves those two lookup values from the current Service Request record at runtime.

The mapped fields should be present on the form. They can be placed in an internal/status section if coordinators should still see the raw values, or hidden if the PCF is the preferred presentation.

## Behavior

The panel shows:

- a coordinator-facing status badge such as `Draft`, `Approval needed`, `Sync failed`, `SLA overdue`, `Close readiness`, or `On track`
- a clear business explanation of the current blocker or next action
- a short next-step checklist
- supporting facts for owner/routing, SLA, approval, sync, response due date, and close readiness

The high-severity close-readiness message mirrors the guardrail requirement: high severity requests need a meaningful resolution summary before closing. The server-side plugin must still enforce that rule.

## Build

```powershell
cd src/pcf/RequestHandlingSummary
npm install
npm run build
```

## Deploy To DEV

```powershell
cd src/pcf/RequestHandlingSummary
pac pcf push --environment https://johnbw-dev.crm3.dynamics.com --solution-unique-name contoso_serviceintake
```

This PAC version accepts either `--publisher-prefix` or `--solution-unique-name`, not both. Targeting the solution is preferred here because the assessment deliverable needs the control packaged in `contoso_serviceintake`.

## Verification

1. Add the PCF control to the internal Service Request form.
2. Map the fields listed above.
3. Open an IT High request with approval pending and confirm the panel recommends manager approval monitoring.
4. Open a request with failed external sync and confirm the panel recommends reviewing the error log and rerunning sync.
5. Open an overdue request and confirm the panel shows an SLA warning or danger state.
6. Open a high severity in-progress request with a short resolution summary and confirm the panel warns about close readiness.
7. Open a healthy routed request and confirm the panel shows `On track`.

## Assessment Delivery

Include this raw PCF source folder in the final package alongside:

- managed solution export
- unpacked solution source
- raw C# plugin source
- README/architecture notes
