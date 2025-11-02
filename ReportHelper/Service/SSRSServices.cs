using System.Net;
using System.Text;
using System.Xml.Linq;
using NLog;
using ReportHelper.Models;

namespace ReportHelper.Services
{
    public class SSRSServices
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly Logger _failedReportLogger = LogManager.GetLogger("FailedReport");

        private readonly string _serviceUrl;
        private readonly NetworkCredential _credentials;

        public SSRSServices(string reportServerUrl, string username, string password)
        {
            _serviceUrl = $"{reportServerUrl.TrimEnd('/')}/ReportService2010.asmx";
            _credentials = new NetworkCredential(username, password);
        }

        public async Task<string> SendSoapAsync(string xml, string action)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Credentials = _credentials,
                    PreAuthenticate = true
                };

                using var client = new HttpClient(handler);
                var request = new HttpRequestMessage(HttpMethod.Post, _serviceUrl)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "text/xml")
                };
                request.Headers.Add("SOAPAction", action);

                var response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string fault = ExtractFault(body);
                    throw new Exception($"SSRS returned {(int)response.StatusCode}: {fault}");
                }

                return body;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"SOAP request failed for {action}");
                throw;
            }
        }

        private static string ExtractFault(string xml)
        {
            try
            {
                var x = XDocument.Parse(xml);
                return x.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value ?? xml;
            }
            catch { return xml; }
        }

        public async Task<bool> CheckConnectionAsync()
        {
            try
            {
                var items = await ListChildrenAsync("/");
                _logger.Info("✅ Connection check succeeded to {url}", _serviceUrl);
                return items.Any();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Connection check failed");
                throw;
            }
        }

        public async Task<List<SSRSItemsModel>> ListChildrenAsync(string path)
        {
            try
            {
                string soap = $@"
<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
  <soap:Body>
    <ListChildren xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
      <ItemPath>{path}</ItemPath>
      <Recursive>false</Recursive>
    </ListChildren>
  </soap:Body>
</soap:Envelope>";

                string xml = await SendSoapAsync(soap,
                    "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/ListChildren");

                XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer";
                return XDocument.Parse(xml)
                    .Descendants(ns + "CatalogItem")
                    .Select(x => new SSRSItemsModel
                    {
                        Name = x.Element(ns + "Name")?.Value ?? "",
                        Path = x.Element(ns + "Path")?.Value ?? "",
                        Type = x.Element(ns + "TypeName")?.Value ?? ""
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to list items at {path}", path);
                throw;
            }
        }

        private static List<string> GetRdlDataSourceNames(byte[] reportDef)
        {
            try
            {
                using var ms = new MemoryStream(reportDef);
                var doc = XDocument.Load(ms);
                var ns = doc.Root?.Name.Namespace ?? "";
                return doc.Descendants(ns + "DataSource")
                          .Select(x => (string?)x.Attribute("Name"))
                          .Where(n => !string.IsNullOrEmpty(n))
                          .Distinct()
                          .ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not read data source names from RDL.");
                return new List<string> { "DataSource1" };
            }
        }
        public async Task UploadReportAsync(
    string folderPath,
    string reportName,
    byte[] reportDef,
    string? dataSourcePath = null,
    bool overwrite = true)
        {
            try
            {
                folderPath = folderPath?.TrimEnd('/') ?? "/";
                reportName = reportName?.Trim() ?? "UnnamedReport";

                if (!string.IsNullOrWhiteSpace(dataSourcePath))
                {
                    reportDef = ReplaceRdlDataSource(reportDef, dataSourcePath);
                }

                string uploadSoap = $@"
<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
  <soap:Body>
    <CreateCatalogItem xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
      <ItemType>Report</ItemType>
      <Name>{System.Security.SecurityElement.Escape(reportName)}</Name>
      <Parent>{System.Security.SecurityElement.Escape(folderPath)}</Parent>
      <Overwrite>{overwrite.ToString().ToLower()}</Overwrite>
      <Definition>{Convert.ToBase64String(reportDef)}</Definition>
      <Properties />
    </CreateCatalogItem>
  </soap:Body>
</soap:Envelope>";

                await SendSoapAsync(uploadSoap,
                    "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/CreateCatalogItem");

                _logger.Info("Uploaded report: {reportName} (Overwrite={overwrite})", reportName, overwrite);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Upload failed for {reportName}", reportName);
                throw new Exception($"Upload failed for '{reportName}': {ex.Message}");
            }
        }


        //        public async Task UploadReportAsync(string folderPath, string reportName, byte[] reportDef, string? dataSourcePath = null)
        //        {
        //            try
        //            {
        //                folderPath = folderPath?.TrimEnd('/') ?? "/";
        //                reportName = reportName?.Trim() ?? "UnnamedReport";

        //                // --- Upload Report ---
        //                string uploadSoap = $@"
        //<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
        //  <soap:Body>
        //    <CreateCatalogItem xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
        //      <ItemType>Report</ItemType>
        //      <Name>{System.Security.SecurityElement.Escape(reportName)}</Name>
        //      <Parent>{System.Security.SecurityElement.Escape(folderPath)}</Parent>
        //      <Overwrite>true</Overwrite>
        //      <Definition>{Convert.ToBase64String(reportDef)}</Definition>
        //      <Properties />
        //    </CreateCatalogItem>
        //  </soap:Body>
        //</soap:Envelope>";

        //                await SendSoapAsync(uploadSoap,
        //                    "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/CreateCatalogItem");

        //                // --- Assign Shared Data Source ---
        //                if (!string.IsNullOrWhiteSpace(dataSourcePath))
        //                {
        //                    var dsNames = GetRdlDataSourceNames(reportDef);
        //                    string dsXml = string.Join("\n", dsNames.Select(n => $@"
        //        <DataSource>
        //          <Name>{System.Security.SecurityElement.Escape(n!)}</Name>
        //          <DataSourceReference>
        //            <Reference>{System.Security.SecurityElement.Escape(dataSourcePath)}</Reference>
        //          </DataSourceReference>
        //        </DataSource>"));

        //                    string reportFullPath = folderPath == "/" ? $"/{reportName}" : $"{folderPath}/{reportName}";

        //                    string assignSoap = $@"
        //<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
        //  <soap:Body>
        //    <SetItemDataSources xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
        //      <ItemPath>{System.Security.SecurityElement.Escape(reportFullPath)}</ItemPath>
        //      <DataSources>
        //        {dsXml}
        //      </DataSources>
        //    </SetItemDataSources>
        //  </soap:Body>
        //</soap:Envelope>";

        //                    await SendSoapAsync(assignSoap,
        //                        "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/SetItemDataSources");
        //                }

        //                _logger.Info("✅ Uploaded report '{reportName}' to '{folderPath}'", reportName, folderPath);
        //            }
        //            catch (Exception ex)
        //            {
        //                string message = $"❌ Failed to upload '{reportName}': {ex.Message}";
        //                _logger.Error(ex, message);
        //                _failedReportLogger.Info(message);
        //                throw new Exception(message);
        //            }
        //        }
        public async Task<List<SSRSItemsModel>> ListDataSourcesAsync(string path = "/")
        {
            try
            {
                string soap = $@"
<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
  <soap:Body>
    <ListChildren xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
      <ItemPath>{path}</ItemPath>
      <Recursive>true</Recursive>
    </ListChildren>
  </soap:Body>
</soap:Envelope>";

                string xml = await SendSoapAsync(
                    soap,
                    "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/ListChildren"
                );

                XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer";

                var dataSources = XDocument.Parse(xml)
                    .Descendants(ns + "CatalogItem")
                    .Where(x => x.Element(ns + "TypeName")?.Value == "DataSource")
                    .Select(x => new SSRSItemsModel
                    {
                        Name = x.Element(ns + "Name")?.Value ?? "",
                        Path = x.Element(ns + "Path")?.Value ?? "",
                        Type = "DataSource"
                    })
                    .ToList();

                _logger.Info("Fetched {count} data sources from path {path}", dataSources.Count, path);
                return dataSources;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to list data sources from path {path}", path);
                throw new Exception($"Failed to list shared data sources: {ex.Message}");
            }
        }

        public async Task<List<SSRSItemsModel>> GetReportsDetailedAsync(string folderPath = "/")
        {
            try
            {
                string soap = $@"
<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
  <soap:Body>
    <ListChildren xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
      <ItemPath>{folderPath}</ItemPath>
      <Recursive>true</Recursive>
    </ListChildren>
  </soap:Body>
</soap:Envelope>";

                string xml = await SendSoapAsync(soap,
                    "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/ListChildren");

                XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer";
                var items = XDocument.Parse(xml)
                    .Descendants(ns + "CatalogItem")
                    .Where(x => x.Element(ns + "TypeName")?.Value == "Report")
                    .Select(x => new SSRSItemsModel
                    {
                        Name = x.Element(ns + "Name")?.Value ?? "",
                        Path = x.Element(ns + "Path")?.Value ?? "",
                        Type = "Report",
                        CreatedDate = x.Element(ns + "CreationDate")?.Value,
                        ModifiedDate = x.Element(ns + "ModifiedDate")?.Value
                    })
                    .ToList();

                _logger.Info("Fetched {count} report metadata items from {path}", items.Count, folderPath);
                return items;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch detailed report list for {path}", folderPath);
                throw new Exception($"Failed to fetch report metadata: {ex.Message}");
            }
        }
        public async Task<Dictionary<string, byte[]>> DownloadReportsAsync(string folderPath)
        {
            var result = new Dictionary<string, byte[]>();

            try
            {
                var reports = await ListChildrenAsync(folderPath);
                var reportList = reports.Where(r => r.Type == "Report").ToList();

                _logger.Info("Preparing to download {count} reports from {path}", reportList.Count, folderPath);

                foreach (var report in reportList)
                {
                    try
                    {
                        string soap = $@"
<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
  <soap:Body>
    <GetItemDefinition xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
      <ItemPath>{System.Security.SecurityElement.Escape(report.Path)}</ItemPath>
    </GetItemDefinition>
  </soap:Body>
</soap:Envelope>";

                        string xml = await SendSoapAsync(
                            soap,
                            "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/GetItemDefinition"
                        );

                        // Extract <Definition> base64 string
                        var doc = XDocument.Parse(xml);
                        var defElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Definition");
                        if (defElement != null)
                        {
                            var defBytes = Convert.FromBase64String(defElement.Value);
                            result[report.Name + ".rdl"] = defBytes;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"❌ Failed to download report: {report.Name}");
                    }
                }

                _logger.Info("✅ Downloaded {count} report definitions from {path}", result.Count, folderPath);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download reports from {path}", folderPath);
                throw new Exception($"Download failed: {ex.Message}");
            }
        }
        private static byte[] ReplaceRdlDataSource(byte[] rdlBytes, string newDataSourcePath)
        {
            try
            {
                using var inputStream = new MemoryStream(rdlBytes);
                var doc = XDocument.Load(inputStream);
                XNamespace ns = doc.Root?.Name.Namespace ?? "";

                var dataSources = doc.Descendants(ns + "DataSource").ToList();
                foreach (var ds in dataSources)
                {
                    // Remove embedded connection info (if any)
                    ds.Elements(ns + "ConnectionProperties").Remove();

                    // Remove existing <DataSourceReference> if present
                    ds.Element(ns + "DataSourceReference")?.Remove();

                    // Add new shared data source reference
                    ds.Add(new XElement(ns + "DataSourceReference", newDataSourcePath));
                }

                using var outputStream = new MemoryStream();
                doc.Save(outputStream);
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while updating RDL data source: {ex.Message}");
            }
        }
        public async Task<List<SSRSItemsModel>> GetReportsAsync(string folderPath)
        {
            try
            {
                string soap = $@"
<soap:Envelope xmlns:soap='http://schemas.xmlsoap.org/soap/envelope/'>
  <soap:Body>
    <ListChildren xmlns='http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer'>
      <ItemPath>{folderPath}</ItemPath>
      <Recursive>false</Recursive>
    </ListChildren>
  </soap:Body>
</soap:Envelope>";

                string xml = await SendSoapAsync(
                    soap,
                    "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer/ListChildren"
                );

                XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2010/03/01/ReportServer";

                var reports = XDocument.Parse(xml)
                    .Descendants(ns + "CatalogItem")
                    .Where(x => x.Element(ns + "TypeName")?.Value == "Report")
                    .Select(x => new SSRSItemsModel
                    {
                        Name = x.Element(ns + "Name")?.Value ?? "",
                        Path = x.Element(ns + "Path")?.Value ?? "",
                        Type = "Report",
                        ModifiedDate = x.Element(ns + "ModifiedDate")?.Value ?? ""
                    })
                    .ToList();

                _logger.Info("Fetched {count} reports from folder {folderPath}", reports.Count, folderPath);
                return reports;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch reports from folder {folderPath}", folderPath);
                throw new Exception($"Failed to fetch reports: {ex.Message}");
            }
        }

    }
}
