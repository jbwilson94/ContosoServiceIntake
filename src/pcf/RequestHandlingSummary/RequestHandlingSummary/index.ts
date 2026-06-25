import { IInputs, IOutputs } from "./generated/ManifestTypes";

interface ParameterLike<TRaw> {
    raw: TRaw | null;
    formatted?: string | null;
}

type Tone = "normal" | "info" | "warning" | "danger" | "success";

interface RuntimeContext extends ComponentFramework.Context<IInputs> {
    page?: {
        entityId?: string;
        entityTypeName?: string;
    };
    mode: ComponentFramework.Mode & {
        contextInfo?: {
            entityId?: string;
            entityTypeName?: string;
        };
    };
}

interface XrmFallback {
    Xrm?: {
        Page?: {
            data?: {
                entity?: {
                    getId?: () => string;
                };
            };
        };
    };
}

interface ActionState {
    tone: Tone;
    label: string;
    title: string;
    message: string;
    nextSteps: string[];
}

interface FactItem {
    label: string;
    value: string;
    detail?: string;
    tone?: Tone;
}

interface ViewModel {
    status: string;
    statusRaw: number | null;
    severity: string;
    severityRaw: number | null;
    routedTeam: LookupDisplay;
    owner: LookupDisplay;
    slaHours: string;
    responseDueOn: string;
    resolutionDueOn: string;
    approvalStatus: string;
    externalSyncStatus: string;
    externalSystemId: string;
    resolutionSummary: string;
    responseDueDate?: Date;
    resolutionDueDate?: Date;
}

interface LookupDisplay {
    id: string;
    name: string;
    entityType: string;
    isSet: boolean;
}

interface AssignmentSnapshot {
    recordId: string;
    owner: LookupDisplay;
    routedTeam: LookupDisplay;
    errorMessage?: string;
}

const STATUS_LABELS: Record<number, string> = {
    100000000: "New",
    100000001: "In Progress",
    100000002: "Closed",
    100000003: "Rejected",
    100000004: "Draft"
};

const SEVERITY_LABELS: Record<number, string> = {
    100000000: "Low",
    100000001: "Medium",
    100000002: "High"
};

const APPROVAL_STATUS_LABELS: Record<number, string> = {
    100000000: "Not Required",
    100000001: "Pending",
    100000002: "Approved",
    100000003: "Rejected"
};

const EXTERNAL_SYNC_STATUS_LABELS: Record<number, string> = {
    100000000: "Not Required",
    100000001: "Pending",
    100000002: "Synced",
    100000003: "Failed"
};

const HIGH_SEVERITY_MIN_RESOLUTION_LENGTH = 20;
const DUE_SOON_HOURS = 8;

/**
 * Internal model-driven app PCF that turns several operational fields into one
 * coordinator-facing action recommendation.
 *
 * The control is deliberately read-only. Routing, ownership, approvals, sync,
 * and close guardrails are still enforced by Dataverse plugins/flows. This PCF
 * exists to help coordinators spot the next business action quickly.
 */
export class RequestHandlingSummary implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container!: HTMLDivElement;
    private assignmentSnapshot?: AssignmentSnapshot;
    private fetchSequence = 0;
    private isDestroyed = false;

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.container = container;
        this.container.classList.add("contoso-action-center-host");
        this.render(context);
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.render(context);
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        this.isDestroyed = true;
        this.container.replaceChildren();
    }

    private render(context: ComponentFramework.Context<IInputs>): void {
        const model = this.toViewModel(context);
        const action = this.getActionState(model);

        const panel = this.createElement("section", `contoso-action-center contoso-action-center--${action.tone}`);
        panel.setAttribute("aria-label", "Request action center");

        panel.append(
            this.createHeader(model, action),
            this.createActionBlock(action),
            this.createFactsGrid(this.getFactItems(model))
        );

        this.container.replaceChildren(panel);
        this.ensureAssignmentSnapshot(context);
    }

    private toViewModel(context: ComponentFramework.Context<IInputs>): ViewModel {
        const statusParameter = context.parameters.status as ParameterLike<number>;
        const severityParameter = context.parameters.severity as ParameterLike<number> | undefined;
        const responseDueParameter = context.parameters.responseDueOn as ParameterLike<Date> | undefined;
        const resolutionDueParameter = context.parameters.resolutionDueOn as ParameterLike<Date> | undefined;
        const owner = this.assignmentSnapshot?.owner ?? this.getPendingLookupDisplay("Assignment loading", true);
        const routedTeam = this.assignmentSnapshot?.routedTeam ?? this.getPendingLookupDisplay("Routing loading", true);

        return {
            status: this.getChoiceLabel(statusParameter, STATUS_LABELS),
            statusRaw: this.getChoiceValue(statusParameter),
            severity: this.getChoiceLabel(severityParameter, SEVERITY_LABELS),
            severityRaw: this.getChoiceValue(severityParameter),
            routedTeam,
            owner,
            slaHours: this.getHoursLabel(context.parameters.slaHours as ParameterLike<number> | undefined),
            responseDueOn: this.getDateLabel(responseDueParameter),
            resolutionDueOn: this.getDateLabel(resolutionDueParameter),
            approvalStatus: this.getChoiceLabel(context.parameters.approvalStatus as ParameterLike<number> | undefined, APPROVAL_STATUS_LABELS),
            externalSyncStatus: this.getChoiceLabel(context.parameters.externalSyncStatus as ParameterLike<number> | undefined, EXTERNAL_SYNC_STATUS_LABELS),
            externalSystemId: this.getText(context.parameters.externalSystemId as ParameterLike<string> | undefined),
            resolutionSummary: this.getText(context.parameters.resolutionSummary as ParameterLike<string> | undefined),
            responseDueDate: this.getDate(responseDueParameter),
            resolutionDueDate: this.getDate(resolutionDueParameter)
        };
    }

    private ensureAssignmentSnapshot(context: ComponentFramework.Context<IInputs>): void {
        const recordId = this.getCurrentRecordId(context);

        if (!recordId || this.assignmentSnapshot?.recordId === recordId || this.isDestroyed) {
            return;
        }

        const sequence = ++this.fetchSequence;
        void context.webAPI
            .retrieveRecord(
                "contoso_servicerequest",
                recordId,
                "?$select=_ownerid_value,_contoso_routedteamid_value"
            )
            .then((row) => {
                if (this.isDestroyed || sequence !== this.fetchSequence) {
                    return undefined;
                }

                this.assignmentSnapshot = {
                    recordId,
                    owner: this.toLookupDisplay(row, "_ownerid_value", "Not assigned"),
                    routedTeam: this.toLookupDisplay(row, "_contoso_routedteamid_value", "Not assigned")
                };

                this.render(context);
                return undefined;
            })
            .catch((error: Error) => {
                if (this.isDestroyed || sequence !== this.fetchSequence) {
                    return undefined;
                }

                this.assignmentSnapshot = {
                    recordId,
                    owner: this.getPendingLookupDisplay("Not loaded", false),
                    routedTeam: this.getPendingLookupDisplay("Not loaded", false),
                    errorMessage: error.message
                };

                this.render(context);
                return undefined;
            });
    }

    private getCurrentRecordId(context: ComponentFramework.Context<IInputs>): string {
        const runtimeContext = context as RuntimeContext;
        const runtimeId = runtimeContext.page?.entityId || runtimeContext.mode.contextInfo?.entityId;

        if (runtimeId) {
            return this.normalizeId(runtimeId);
        }

        // The classic Xrm.Page API is only used as a guarded fallback because the
        // public PCF typings do not expose the host form record id consistently.
        const xrmId = (window as XrmFallback).Xrm?.Page?.data?.entity?.getId?.();
        return this.normalizeId(xrmId);
    }

    private toLookupDisplay(row: ComponentFramework.WebApi.Entity, propertyName: string, fallbackName: string): LookupDisplay {
        const id = this.normalizeId(row[propertyName] as string | undefined);
        const formattedName = row[`${propertyName}@OData.Community.Display.V1.FormattedValue`] as string | undefined;
        const entityType = row[`${propertyName}@Microsoft.Dynamics.CRM.lookuplogicalname`] as string | undefined;

        return {
            id,
            name: formattedName || fallbackName,
            entityType: entityType || "",
            isSet: id !== ""
        };
    }

    private getPendingLookupDisplay(name: string, isSet: boolean): LookupDisplay {
        return {
            id: "",
            name,
            entityType: "",
            isSet
        };
    }

    private getActionState(model: ViewModel): ActionState {
        const closeGuardrailApplies = this.isHighSeverity(model) && !this.hasMeaningfulResolution(model);
        const ownerMismatch = this.hasOwnerMismatch(model);

        if (model.status === "Closed") {
            return {
                tone: "success",
                label: "Completed",
                title: "Request is closed",
                message: "No coordinator action is currently required.",
                nextSteps: this.getClosedFollowUps(model)
            };
        }

        if (model.status === "Rejected") {
            return {
                tone: "normal",
                label: "Rejected",
                title: "Request is rejected",
                message: "The request is no longer active. Review the reason before reopening or duplicating the request.",
                nextSteps: ["Confirm the requester has enough context about the rejection decision."]
            };
        }

        if (model.status === "Draft") {
            return {
                tone: "info",
                label: "Draft",
                title: "Portal submission is not complete",
                message: "The request was created by the multi-step form, but the requester has not reached the final submit state yet.",
                nextSteps: [
                    "Do not begin internal fulfillment until the status changes to New.",
                    "If the requester cannot complete submission, review whether the draft should be cleaned up or followed up."
                ]
            };
        }

        if (!model.routedTeam.isSet || !model.owner.isSet) {
            return {
                tone: "warning",
                label: "Routing incomplete",
                title: "Routing has not fully assigned the request",
                message: "The routing rule, owner team, or form mapping is incomplete. Coordinators should not start fulfillment until ownership is clear.",
                nextSteps: [
                    "Confirm Category and Severity are populated.",
                    "Confirm an active Routing/SLA Rule exists for this combination.",
                    "Save the request again after correcting the routing configuration."
                ]
            };
        }

        if (ownerMismatch) {
            return {
                tone: "warning",
                label: "Owner mismatch",
                title: "Owner does not match the routed team",
                message: "The request was routed to one team but is currently owned by another. This can cause missed queue work or reporting drift.",
                nextSteps: [
                    `Reassign the owner to ${model.routedTeam.name}, or update the routing rule if the new owner is intentional.`,
                    "Leave a note explaining any manual reassignment."
                ]
            };
        }

        if (model.approvalStatus === "Rejected") {
            return {
                tone: "danger",
                label: "Approval rejected",
                title: "Manager approval was rejected",
                message: "This request should not proceed until the rejection is reviewed and the requester or coordinator takes corrective action.",
                nextSteps: [
                    "Review the approval comments.",
                    "Set the request to Rejected or revise the request and resubmit approval."
                ]
            };
        }

        if (model.externalSyncStatus === "Failed") {
            return {
                tone: "danger",
                label: "Sync failed",
                title: "External system synchronization failed",
                message: "The approved request did not sync successfully to the downstream system.",
                nextSteps: [
                    "Open the related Contoso error log record.",
                    "Correct the failed payload or downstream issue, then rerun the sync process."
                ]
            };
        }

        if (this.isPastDue(model.resolutionDueDate)) {
            return {
                tone: "danger",
                label: "SLA overdue",
                title: "Resolution SLA is overdue",
                message: "This request has passed its resolution target and needs immediate coordinator attention.",
                nextSteps: [
                    "Confirm the owning team is actively working the request.",
                    "Escalate with the team manager if the request cannot be resolved today."
                ]
            };
        }

        if (this.isPastDue(model.responseDueDate)) {
            return {
                tone: "warning",
                label: "Response overdue",
                title: "First response is overdue",
                message: "The request has not met its expected initial response timing.",
                nextSteps: [
                    "Ask the owner team to acknowledge the request.",
                    "Update status and add a note once the customer has been contacted."
                ]
            };
        }

        if (model.approvalStatus === "Pending") {
            return {
                tone: "warning",
                label: "Approval needed",
                title: "Waiting for manager approval",
                message: "This request should be reviewed before downstream processing or external synchronization proceeds.",
                nextSteps: [
                    "Monitor the approval outcome.",
                    "Do not close or sync the request until approval is complete."
                ]
            };
        }

        if (model.externalSyncStatus === "Pending") {
            return {
                tone: "warning",
                label: "Sync pending",
                title: "External synchronization is pending",
                message: "The request is approved or ready for downstream handoff, but the external system has not acknowledged it yet.",
                nextSteps: [
                    "Confirm the integration flow has started.",
                    "Watch for an external ID or an error log entry."
                ]
            };
        }

        if (closeGuardrailApplies) {
            return {
                tone: "info",
                label: "Close readiness",
                title: "High severity close guardrail will apply",
                message: `High severity requests need a meaningful resolution summary of at least ${HIGH_SEVERITY_MIN_RESOLUTION_LENGTH} characters before they can be closed.`,
                nextSteps: [
                    "Capture the resolution outcome before attempting to close.",
                    "Include enough detail for audit and manager review."
                ]
            };
        }

        if (this.isDueSoon(model.resolutionDueDate)) {
            return {
                tone: "info",
                label: "Due soon",
                title: "Resolution target is approaching",
                message: "The request is healthy, but the resolution target is close enough to watch.",
                nextSteps: [
                    "Confirm the owner team has a clear next action.",
                    "Update notes if the request is waiting on the requester."
                ]
            };
        }

        return {
            tone: "success",
            label: "On track",
            title: "No immediate coordinator action",
            message: "Routing, ownership, SLA, approval, and sync signals are currently aligned.",
            nextSteps: ["Continue normal monitoring through the assigned owner team."]
        };
    }

    private getFactItems(model: ViewModel): FactItem[] {
        return [
            {
                label: "Owner",
                value: model.owner.name,
                detail: model.routedTeam.isSet ? `Routed to ${model.routedTeam.name}` : "Routing team not assigned",
                tone: this.hasOwnerMismatch(model) ? "warning" : "normal"
            },
            {
                label: "SLA",
                value: model.resolutionDueOn,
                detail: this.getDueDetail(model.resolutionDueDate, model.status, model.slaHours),
                tone: this.getDueTone(model.resolutionDueDate, model.status)
            },
            {
                label: "Approval",
                value: model.approvalStatus,
                detail: model.severity === "High" ? "High severity path" : `${model.severity} severity path`,
                tone: this.getApprovalTone(model.approvalStatus)
            },
            {
                label: "External sync",
                value: model.externalSyncStatus,
                detail: model.externalSystemId ? `External ID ${model.externalSystemId}` : "No external ID",
                tone: this.getExternalSyncTone(model.externalSyncStatus)
            },
            {
                label: "Response",
                value: model.responseDueOn,
                detail: this.getDueDetail(model.responseDueDate, model.status),
                tone: this.getDueTone(model.responseDueDate, model.status)
            },
            {
                label: "Close readiness",
                value: this.getCloseReadinessLabel(model),
                detail: this.getCloseReadinessDetail(model),
                tone: this.getCloseReadinessTone(model)
            }
        ];
    }

    private createHeader(model: ViewModel, action: ActionState): HTMLElement {
        const header = this.createElement("div", "contoso-action-center__header");
        const titleBlock = this.createElement("div", "contoso-action-center__title-block");

        titleBlock.append(
            this.createTextElement("div", "contoso-action-center__eyebrow", "Coordinator workspace"),
            this.createTextElement("div", "contoso-action-center__title", "Request action center"),
            this.createTextElement("div", "contoso-action-center__subtitle", `${model.status} · ${model.severity} severity`)
        );

        const badge = this.createTextElement("span", `contoso-action-center__badge contoso-action-center__badge--${action.tone}`, action.label);
        header.append(titleBlock, badge);
        return header;
    }

    private createActionBlock(action: ActionState): HTMLElement {
        const block = this.createElement("div", `contoso-action-center__action contoso-action-center__action--${action.tone}`);
        const copy = this.createElement("div", "contoso-action-center__action-copy");

        copy.append(
            this.createTextElement("div", "contoso-action-center__action-title", action.title),
            this.createTextElement("p", "contoso-action-center__action-message", action.message)
        );

        const list = this.createElement("ul", "contoso-action-center__steps");
        for (const step of action.nextSteps) {
            list.append(this.createTextElement("li", "contoso-action-center__step", step));
        }

        block.append(copy, list);
        return block;
    }

    private createFactsGrid(items: FactItem[]): HTMLElement {
        const grid = this.createElement("dl", "contoso-action-center__facts");

        for (const item of items) {
            const wrapper = this.createElement("div", `contoso-action-center__fact ${this.getFactToneClass(item.tone)}`);
            wrapper.append(
                this.createTextElement("dt", "contoso-action-center__fact-label", item.label),
                this.createTextElement("dd", "contoso-action-center__fact-value", item.value),
                this.createTextElement("dd", "contoso-action-center__fact-detail", item.detail ?? "")
            );
            grid.append(wrapper);
        }

        return grid;
    }

    private getClosedFollowUps(model: ViewModel): string[] {
        if (model.externalSyncStatus === "Failed") {
            return ["Resolve the failed external sync even though the request is closed."];
        }

        if (this.isHighSeverity(model) && !this.hasMeaningfulResolution(model)) {
            return ["Review the close guardrail configuration; high severity requests should include a meaningful resolution summary."];
        }

        return ["Confirm final notes and attachments are complete for audit history."];
    }

    private getCloseReadinessLabel(model: ViewModel): string {
        if (!this.isHighSeverity(model)) {
            return "Not restricted";
        }

        return this.hasMeaningfulResolution(model) ? "Ready to close" : "Needs summary";
    }

    private getCloseReadinessDetail(model: ViewModel): string {
        if (!this.isHighSeverity(model)) {
            return "Guardrail applies only to high severity";
        }

        const length = this.getResolutionLength(model);
        return `${length}/${HIGH_SEVERITY_MIN_RESOLUTION_LENGTH} resolution characters`;
    }

    private getCloseReadinessTone(model: ViewModel): Tone {
        if (!this.isHighSeverity(model)) {
            return "normal";
        }

        return this.hasMeaningfulResolution(model) ? "success" : "warning";
    }

    private getDueDetail(date: Date | undefined, status: string, slaHours?: string): string {
        if (!date) {
            return slaHours && slaHours !== "Not set" ? `${slaHours} target` : "No due date";
        }

        if (this.isTerminalStatus(status)) {
            return "Terminal request";
        }

        const delta = date.getTime() - Date.now();
        if (delta < 0) {
            return `Past due by ${this.formatDuration(Math.abs(delta))}`;
        }

        return `Due in ${this.formatDuration(delta)}`;
    }

    private getDueTone(date: Date | undefined, status: string): Tone {
        if (!date || this.isTerminalStatus(status)) {
            return "normal";
        }

        if (this.isPastDue(date)) {
            return "danger";
        }

        return this.isDueSoon(date) ? "warning" : "normal";
    }

    private getApprovalTone(status: string): Tone {
        if (status === "Pending") {
            return "warning";
        }

        if (status === "Rejected") {
            return "danger";
        }

        if (status === "Approved" || status === "Not Required") {
            return "success";
        }

        return "normal";
    }

    private getExternalSyncTone(status: string): Tone {
        if (status === "Pending") {
            return "warning";
        }

        if (status === "Failed") {
            return "danger";
        }

        if (status === "Synced" || status === "Not Required") {
            return "success";
        }

        return "normal";
    }

    private getFactToneClass(tone?: Tone): string {
        return tone && tone !== "normal" ? `contoso-action-center__fact--${tone}` : "";
    }

    private hasOwnerMismatch(model: ViewModel): boolean {
        return model.owner.isSet
            && model.routedTeam.isSet
            && model.owner.id !== ""
            && model.routedTeam.id !== ""
            && model.owner.id !== model.routedTeam.id;
    }

    private hasMeaningfulResolution(model: ViewModel): boolean {
        return this.getResolutionLength(model) >= HIGH_SEVERITY_MIN_RESOLUTION_LENGTH;
    }

    private getResolutionLength(model: ViewModel): number {
        return model.resolutionSummary.trim().length;
    }

    private isHighSeverity(model: ViewModel): boolean {
        return model.severity === "High" || model.severityRaw === 100000002;
    }

    private isTerminalStatus(status: string): boolean {
        return status === "Closed" || status === "Rejected";
    }

    private isPastDue(date: Date | undefined): boolean {
        return date !== undefined && date.getTime() < Date.now();
    }

    private isDueSoon(date: Date | undefined): boolean {
        if (!date) {
            return false;
        }

        const dueSoonMs = DUE_SOON_HOURS * 60 * 60 * 1000;
        const delta = date.getTime() - Date.now();
        return delta >= 0 && delta <= dueSoonMs;
    }

    private getChoiceLabel(parameter: ParameterLike<number> | undefined, labels: Record<number, string>): string {
        if (!parameter) {
            return "Not set";
        }

        if (parameter.formatted) {
            return parameter.formatted;
        }

        return parameter.raw === null ? "Not set" : labels[parameter.raw] ?? String(parameter.raw);
    }

    private getChoiceValue(parameter: ParameterLike<number> | undefined): number | null {
        return parameter?.raw ?? null;
    }

    private getHoursLabel(parameter: ParameterLike<number> | undefined): string {
        if (!parameter || parameter.raw === null) {
            return "Not set";
        }

        return parameter.formatted || `${parameter.raw} hours`;
    }

    private getText(parameter: ParameterLike<string> | undefined): string {
        return parameter?.raw || parameter?.formatted || "";
    }

    private getDate(parameter: ParameterLike<Date> | undefined): Date | undefined {
        return parameter?.raw ?? undefined;
    }

    private getDateLabel(parameter: ParameterLike<Date> | undefined): string {
        if (!parameter) {
            return "Not set";
        }

        if (parameter.formatted) {
            return parameter.formatted;
        }

        return parameter.raw ? parameter.raw.toLocaleString() : "Not set";
    }

    private normalizeId(id: string | undefined): string {
        return (id || "").replace(/[{}]/g, "").toLowerCase();
    }

    private formatDuration(milliseconds: number): string {
        const totalMinutes = Math.max(1, Math.ceil(milliseconds / 60000));

        if (totalMinutes < 60) {
            return `${totalMinutes} min`;
        }

        const totalHours = Math.ceil(totalMinutes / 60);
        if (totalHours < 48) {
            return `${totalHours} hr`;
        }

        return `${Math.ceil(totalHours / 24)} days`;
    }

    private createElement(tagName: string, className: string): HTMLElement {
        const element = document.createElement(tagName);
        element.className = className;
        return element;
    }

    private createTextElement(tagName: string, className: string, text: string): HTMLElement {
        const element = this.createElement(tagName, className);
        element.textContent = text;
        return element;
    }
}
