using Microsoft.AspNetCore.Mvc;
using ReportHelper.Models;
using ReportHelper.Services;
using System.Text;
using System.Xml.Linq;

namespace ReportHelper.Controllers
{
    public class SSRSController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> CheckConnection(SSRSConnectionModel model)
        {
            try
            {
                var ssrs = new SSRSServices(model.ReportServerUrl, model.Username, model.Password);
                if (!await ssrs.CheckConnectionAsync())
                {
                    TempData["ErrorMsg"] = "❌ Could not connect to SSRS. Check credentials or URL.";
                    return View("Index", model);
                }

                TempData["SuccessMsg"] = "✅ Connection successful!";
                ViewBag.Connection = model;
                ViewBag.DataSources = await ssrs.ListDataSourcesAsync("/");
                var folders = await ssrs.ListChildrenAsync("/");
                return View("Upload", folders);
            }
            catch (Exception ex)
            {
                TempData["ErrorMsg"] = ex.Message;
                return View("Index", model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadReports(string folderPath,List<IFormFile> files,string serverUrl,string username,string password,string? dataSourcePath,bool overwrite = true)
        {
            try
            {
                if (files == null || files.Count == 0)
                {
                    TempData["ErrorMsg"] = "Please select at least one .rdl file.";
                    return RedirectToAction("Index");
                }

                if (files.Count > 200)
                    files = files.Take(200).ToList();

                var ssrs = new SSRSServices(serverUrl, username, password);

                int successCount = 0;
                int failCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        await ssrs.UploadReportAsync(folderPath, Path.GetFileNameWithoutExtension(file.FileName), ms.ToArray(), dataSourcePath, overwrite);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        NLog.LogManager.GetLogger("FailedReport").Error($"Failed to upload {file.FileName}: {ex.Message}");
                    }
                }

                TempData["UploadMsg"] = $"✅ Uploaded {successCount} reports successfully. {(failCount > 0 ? $"{failCount} failed (see logs)." : "")}";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMsg"] = $"❌ Upload failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        //public async Task<IActionResult> UploadReports(
        //    string folderPath, string dataSourcePath,
        //    List<IFormFile> files, string serverUrl, string username, string password)
        //{
        //    if (files == null || files.Count == 0)
        //    {
        //        TempData["ErrorMsg"] = "⚠️ Please select at least one .rdl file.";
        //        return RedirectToAction("Index");
        //    }

        //    int success = 0;
        //    var ssrs = new SSRSServices(serverUrl, username, password);

        //    foreach (var file in files.Take(200))
        //    {
        //        try
        //        {
        //            using var ms = new MemoryStream();
        //            await file.CopyToAsync(ms);
        //            await ssrs.UploadReportAsync(folderPath, Path.GetFileNameWithoutExtension(file.FileName), ms.ToArray(), dataSourcePath);
        //            success++;
        //        }
        //        catch (Exception ex)
        //        {
        //            TempData["ErrorMsg"] = $"❌ {file.FileName}: {ex.Message}";
        //        }
        //    }

        //    TempData["SuccessMsg"] = $"✅ {success} report(s) uploaded successfully.";
        //    return RedirectToAction("Index");
        //}
        [HttpPost]
        public async Task<IActionResult> ExportReports(
    string folderPath, string serverUrl, string username, string password, string format = "excel")
        {
            try
            {
                var ssrs = new SSRSServices(serverUrl, username, password);
                var reports = await ssrs.GetReportsDetailedAsync(folderPath);

                if (!reports.Any())
                {
                    TempData["ErrorMsg"] = "⚠️ No reports found to export.";
                    return RedirectToAction("Index");
                }

                if (format.ToLower() == "csv")
                {
                    var csvBytes = GenerateCsv(reports);
                    return File(csvBytes, "text/csv", $"SSRSReports_{DateTime.Now:yyyyMMddHHmm}.csv");
                }
                else
                {
                    var excelBytes = GenerateExcel(reports);
                    return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"SSRSReports_{DateTime.Now:yyyyMMddHHmm}.xlsx");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMsg"] = $"❌ Export failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
        private byte[] GenerateCsv(List<SSRSItemsModel> reports)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Report Name,Path,Data Source Path,Created Date,Modified Date");

            foreach (var r in reports)
            {
                sb.AppendLine($"\"{r.Name}\",\"{r.Path}\",\"{r.DataSourcePath}\",\"{r.CreatedDate}\",\"{r.ModifiedDate}\"");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }


        private byte[] GenerateExcel(List<SSRSItemsModel> reports)
        {
            using var ms = new MemoryStream();
            using (var spreadsheet = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = spreadsheet.AddWorkbookPart();
                workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();

                var sheetPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
                var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();

                // Header row
                var header = new DocumentFormat.OpenXml.Spreadsheet.Row();
                string[] headers = { "Report Name", "Path", "Data Source Path", "Created Date", "Modified Date" };
                foreach (var h in headers)
                {
                    header.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
                    {
                        DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
                        CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(h)
                    });
                }
                sheetData.Append(header);

                // Data rows
                foreach (var r in reports)
                {
                    var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
                    row.Append(
                        CreateCell(r.Name),
                        CreateCell(r.Path),
                        CreateCell(r.DataSourcePath),
                        CreateCell(r.CreatedDate),
                        CreateCell(r.ModifiedDate)
                    );
                    sheetData.Append(row);
                }

                sheetPart.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(sheetData);
                var sheets = spreadsheet.WorkbookPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
                sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
                {
                    Id = spreadsheet.WorkbookPart.GetIdOfPart(sheetPart),
                    SheetId = 1,
                    Name = "SSRS Reports"
                });

                workbookPart.Workbook.Save();
            }
            return ms.ToArray();
        }

        private DocumentFormat.OpenXml.Spreadsheet.Cell CreateCell(string? value)
        {
            return new DocumentFormat.OpenXml.Spreadsheet.Cell
            {
                DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
                CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(value ?? "")
            };
        }
        [HttpPost]
        public async Task<IActionResult> DownloadAll(string folderPath,string serverUrl,string username,string password)
        {
            try
            {
                var ssrs = new SSRSServices(serverUrl, username, password);
                var reports = await ssrs.DownloadReportsAsync(folderPath);

                if (reports.Count == 0)
                {
                    TempData["ErrorMsg"] = "⚠️ No reports found to download in the selected folder.";
                    return RedirectToAction("Index");
                }

                // Create ZIP
                using var ms = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    foreach (var item in reports)
                    {
                        var entry = archive.CreateEntry(item.Key, System.IO.Compression.CompressionLevel.Fastest);
                        using var entryStream = entry.Open();
                        await entryStream.WriteAsync(item.Value, 0, item.Value.Length);
                    }
                }


                return File(ms.ToArray(), "application/zip", $"SSRS_Reports_{DateTime.Now:yyyyMMddHHmm}.zip");
            }
            catch (Exception ex)
            {
                TempData["ErrorMsg"] = $"❌ Download failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
        [HttpPost]
        public async Task<IActionResult> DownloadSelected(string folderPath,string[] selectedReports,string serverUrl,string username,string password)
        {
            try
            {
                if (selectedReports == null || selectedReports.Length == 0)
                {
                    TempData["ErrorMsg"] = "⚠️ No reports selected for download.";
                    return RedirectToAction("Index");
                }

                var ssrs = new SSRSServices(serverUrl, username, password);
                using var ms = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    foreach (var reportName in selectedReports)
                    {
                        try
                        {
                            string soap = $@"
<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
  <soap:Body>
    <GetItemDefinition xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
      <ItemPath>{System.Security.SecurityElement.Escape(folderPath + "/" + reportName)}</ItemPath>
    </GetItemDefinition>
  </soap:Body>
</soap:Envelope>";

                            string xml = await ssrs.SendSoapAsync(soap,
                                "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/GetItemDefinition");

                            var doc = XDocument.Parse(xml);
                            var def = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Definition")?.Value;
                            if (def != null)
                            {
                                var bytes = Convert.FromBase64String(def);
                                var entry = archive.CreateEntry(reportName + ".rdl");
                                using var entryStream = entry.Open();
                                await entryStream.WriteAsync(bytes);
                            }
                        }
                        catch (Exception ex)
                        {
                            NLog.LogManager.GetLogger("FailedReport").Error($"Failed to download {reportName}: {ex.Message}");
                        }
                    }
                }

                return File(ms.ToArray(), "application/zip", $"SelectedReports_{DateTime.Now:yyyyMMddHHmm}.zip");
            }
            catch (Exception ex)
            {
                TempData["ErrorMsg"] = $"❌ Download failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetReportsList(string folderPath,string serverUrl,string username,string password)
        {
            try
            {
                var ssrs = new SSRSServices(serverUrl, username, password);
                var reports = await ssrs.GetReportsAsync(folderPath);
                return Json(reports.Select(r => new { r.Name, r.Path, r.ModifiedDate }));
            }
            catch (Exception ex)
            {
                return Json(new { error = $"❌ Failed to fetch reports: {ex.Message}" });
            }
        }

    }
}
