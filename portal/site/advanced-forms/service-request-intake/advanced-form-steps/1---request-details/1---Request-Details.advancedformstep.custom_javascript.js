$(document).ready(function() {
    // Add shared secondary navigation beside the generated advanced form action button.
    if (window.ContosoCommon) {
        window.ContosoCommon.addBackToHomeButton({
            beforeSelector: "#NextButton, #SubmitButton"
        });

        window.ContosoCommon.makeRequired("#contoso_title");
        window.ContosoCommon.makeRequired("#contoso_categoryid");
        window.ContosoCommon.makeRequired("#contoso_severity");
        window.ContosoCommon.makeRequired("#contoso_requesteremail");
        window.ContosoCommon.makeRequired("#contoso_description");
    }

    // Mount the routing/SLA preview inside the Details section near the fields that drive routing.
    var detailsSection = $('[data-name="tab_details_section_details"]').first();
    if (!detailsSection.length || $("#contoso-routing-preview").length) {
        return;
    }

    var preview = $("<div>", {
        id: "contoso-routing-preview",
        class: "contoso-routing-preview is-empty",
        role: "status",
        "aria-live": "polite"
    });

    if (detailsSection.is("table")) {
        var previewRow = $("<tr>", {
            id: "contoso-routing-preview-row"
        });
        var previewCell = $("<td>", {
            colspan: 10,
            class: "cell"
        });

        previewCell.append(preview);
        previewRow.append(previewCell);
        detailsSection.find("tbody").first().prepend(previewRow);
    } else {
        detailsSection.prepend(preview);
    }

    var lastRoutingKey = "";
    var activeRequest = null;

    // Render simple status text for initial, loading, warning, and error states.
    function renderMessage(message, stateClass) {
        preview
            .removeClass("is-empty is-loading is-ready is-warning is-error")
            .addClass(stateClass || "is-empty")
            .empty()
            .append($("<div>", {
                class: "contoso-routing-preview-message",
                text: message
            }));
    }

    // Render the successful routing result returned by /fetch-routing.
    function renderRoutingResult(result) {
        if (!result || !result.matched) {
            renderMessage(result && result.message ? result.message : "No routing rule was found for this request.", "is-warning");
            return;
        }

        preview
            .removeClass("is-empty is-loading is-warning is-error")
            .addClass("is-ready")
            .empty()
            .append($("<div>", {
                class: "contoso-routing-preview-grid"
            })
                .append($("<div>", {
                    class: "contoso-routing-preview-item"
                })
                    .append($("<div>", {
                        class: "contoso-routing-preview-label",
                        text: "Routed team"
                    }))
                    .append($("<div>", {
                        class: "contoso-routing-preview-value",
                        text: result.teamName || "Not available"
                    })))
                .append($("<div>", {
                    class: "contoso-routing-preview-item"
                })
                    .append($("<div>", {
                        class: "contoso-routing-preview-label",
                        text: "Resolution target"
                    }))
                    .append($("<div>", {
                        class: "contoso-routing-preview-value",
                        text: result.slaHours ? result.slaHours + " hours" : "Not available"
                    })))
                .append($("<div>", {
                    class: "contoso-routing-preview-item"
                })
                    .append($("<div>", {
                        class: "contoso-routing-preview-label",
                        text: "Approval"
                    }))
                    .append($("<div>", {
                        class: "contoso-routing-preview-value",
                        text: result.approvalRequired ? "Required" : "Not required"
                    })))
                .append($("<div>", {
                    class: "contoso-routing-preview-item"
                })
                    .append($("<div>", {
                        class: "contoso-routing-preview-label",
                        text: "External sync"
                    }))
                    .append($("<div>", {
                        class: "contoso-routing-preview-value",
                        text: result.externalSyncRequired ? "Required" : "Not required"
                    }))));

        if (result.message) {
            preview.append($("<p>", {
                class: "contoso-routing-preview-note",
                text: result.message
            }));
        }
    }

    // The category lookup stores the selected record id in the hidden contoso_categoryid input.
    function getRoutingKey() {
        var categoryId = ($("#contoso_categoryid").val() || "").replace(/[{}]/g, "");
        var severity = $("#contoso_severity").val() || "";

        return categoryId && severity ? categoryId + "|" + severity : "";
    }

    // Call the Liquid JSON endpoint whenever category or severity changes.
    function refreshRoutingPreview() {
        var routingKey = getRoutingKey();
        if (routingKey === lastRoutingKey) {
            return;
        }

        lastRoutingKey = routingKey;

        if (!routingKey) {
            renderMessage("Select a category and severity to preview how this request will be routed.", "is-empty");
            return;
        }

        var parts = routingKey.split("|");
        renderMessage("Checking how this request will be routed...", "is-loading");

        if (activeRequest) {
            activeRequest.abort();
        }

        activeRequest = $.ajax({
            url: "/fetch-routing",
            method: "GET",
            dataType: "json",
            cache: false,
            data: {
                categoryId: parts[0],
                severity: parts[1]
            }
        })
            .done(renderRoutingResult)
            .fail(function(xhr, status) {
                if (status === "abort") {
                    return;
                }

                renderMessage("Routing preview could not be loaded right now.", "is-error");
            });
    }

    // Lookup controls do not always fire change events on the hidden id input, so poll lightly as a fallback.
    $("#contoso_categoryid, #contoso_categoryid_name, #contoso_severity").on("change input blur", refreshRoutingPreview);
    renderMessage("Select a category and severity to preview how this request will be routed.", "is-empty");
    window.setInterval(refreshRoutingPreview, 750);
});
