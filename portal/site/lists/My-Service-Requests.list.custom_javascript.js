(function () {
    $(document).ready(function () {
        addBackToHomeButton();

        // Power Pages lists re-render after initial load, paging, sorting, or filtering.
        // The list's "loaded" event is the reliable point to apply row-level styling.
        $(".entity-grid").on("loaded", function () {
            var $grid = $(this);
            var severityIndex = getSeverityColumnIndex($grid);

            if (severityIndex < 0) {
                return;
            }

            $grid.find("tbody tr").each(function () {
                applySeverityClass($(this), severityIndex);
            });
        });
    });

    // Insert one page-level action below the generated list. This does not wait
    // for the grid "loaded" event because the surrounding list container is
    // available during the normal document-ready lifecycle.
    function addBackToHomeButton() {
        var $anchor = $(".entity-grid, .entitylist").first();
        var $buttonRow;
        var $button;

        if (!$anchor.length || $("#contoso-my-requests-back-home").length) {
            return;
        }

        $button = $("<button>", {
            id: "contoso-my-requests-back-home",
            type: "button",
            class: "btn btn-secondary",
            text: "Back to Home"
        });

        $button.on("click", function () {
            window.location.href = "/";
        });

        $buttonRow = $("<div>", {
            class: "contoso-list-actions d-flex flex-wrap gap-2 mt-4"
        }).append($button);

        $anchor.after($buttonRow);
    }

    // Find the visible Severity column instead of hard-coding a column number.
    function getSeverityColumnIndex($grid) {
        return $grid.find("thead th").filter(function () {
            return $(this).text().trim().toLowerCase().indexOf("severity") >= 0;
        }).first().index();
    }

    // Add one simple class that the global Contoso theme uses for row colors.
    function applySeverityClass($row, severityIndex) {
        var severity = $row.children("td").eq(severityIndex).text().trim().toLowerCase();

        $row
            .removeClass("contoso-severity-low contoso-severity-medium contoso-severity-high")
            .addClass(getSeverityClass(severity));
    }

    // Only known severity values receive row styling.
    function getSeverityClass(severity) {
        if (severity === "low" || severity === "medium" || severity === "high") {
            return "contoso-severity-" + severity;
        }

        return "";
    }
})();
