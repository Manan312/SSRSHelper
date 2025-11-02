// SSRS Reports Loader Script
// Shows reports in a 3-column table: Select | Name | Last Modified

$(document).ready(function () {

    $("#fetchReportsBtn").click(function () {
        const folderPath = $("input[name='folderPath']:checked").val();
        const serverUrl = $("input[name='serverUrl']").val();
        const username = $("input[name='username']").val();
        const password = $("input[name='password']").val();

        if (!folderPath) {
            showMessage("⚠️ Please select a folder first.", "warning");
            return;
        }

        showMessage("⏳ Fetching reports from server...", "info");

        $.ajax({
            url: "/SSRS/GetReportsList",
            type: "POST",
            data: {
                folderPath: folderPath,
                serverUrl: serverUrl,
                username: username,
                password: password
            },
            success: function (data) {
                if (data.error) {
                    showMessage(data.error, "danger");
                    return;
                }

                if (!data || data.length === 0) {
                    showMessage("No reports found in this folder.", "muted");
                    return;
                }

                // Build table
                let html = `
                    <div class='d-flex justify-content-between mb-2'>
                        <button type='button' class='btn btn-sm btn-outline-secondary' id='selectAllBtn'>Select All</button>
                        <span class='text-muted small'>${data.length} report(s) found</span>
                    </div>
                    <table class='table table-bordered table-hover align-middle'>
                        <thead class='table-light'>
                            <tr>
                                <th style='width: 60px;'>Select</th>
                                <th>Report Name</th>
                                <th style='width: 200px;'>Last Modified</th>
                            </tr>
                        </thead>
                        <tbody>`;

                data.forEach(r => {
                    const modified = r.ModifiedDate ? new Date(r.ModifiedDate).toLocaleString() : "—";
                    html += `
                        <tr>
                            <td class='text-center'>
                                <input class='form-check-input' type='checkbox' name='selectedReports' value='${r.Name}' />
                            </td>
                            <td>${r.Name}</td>
                            <td>${modified}</td>
                        </tr>`;
                });

                html += "</tbody></table>";

                $("#reportsList").html(html);

                // Handle Select All toggle
                $("#selectAllBtn").click(function () {
                    const allChecked = $("input[name='selectedReports']:checked").length === data.length;
                    $("input[name='selectedReports']").prop("checked", !allChecked);
                    $(this).text(allChecked ? "Select All" : "Deselect All");
                });
            },
            error: function (xhr, status, error) {
                showMessage("❌ Failed to load reports. Check connection or credentials.<br/>" + error, "danger");
            }
        });
    });

    // Helper for showing messages
    function showMessage(message, type) {
        const color = type === "muted" ? "secondary" : type;
        $("#reportsList").html(`<div class='alert alert-${color}'>${message}</div>`);
    }
});
