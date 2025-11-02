# 📊 SSRS Report Helper (MVC .NET 8)

A lightweight ASP.NET Core MVC application to **upload, modify, and download SQL Server Reporting Services (SSRS)** reports programmatically — no SSRS Designer required.

---

## 🚀 Features

| Feature | Description |
|----------|--------------|
| 🔑 **SSRS Connection Check** | Validate SSRS server, credentials, and connectivity before performing any action. |
| 📤 **Upload Reports** | Upload up to 200 `.rdl` reports at a time to the SSRS server. |
| ♻️ **Overwrite Existing Reports** | Option to overwrite reports if they already exist on the server. |
| 🔗 **Assign Shared Data Source** | Automatically modify `.rdl` files to point to selected shared data sources. |
| 📥 **Download Reports** | Download selected or all reports from a chosen folder as a ZIP archive. |
| 🗂️ **View Report Metadata** | Display report name, modified date, and other metadata. |
| 🧾 **NLog Integration** | Detailed logging for connection, uploads, downloads, and failed reports. |
| 🪶 **Bootstrap UI + AJAX** | Modern, responsive UI with real-time report fetching. |

---

## 🧩 Tech Stack

- **Framework:** .NET 8 (ASP.NET Core MVC)
- **Language:** C#
- **Frontend:** Bootstrap 5, jQuery
- **Logging:** NLog
- **SSRS API:** ReportService2010 SOAP Web Service
- **Build Target:** Windows / Linux / Docker compatible

---

## 🧰 Prerequisites

| Requirement | Version |
|--------------|----------|
| Visual Studio / VS Code | 2022 or later |
| .NET SDK | 8.0 or later |
| SQL Server Reporting Services | 2016+ |
| Access to ReportServer URL | Example: `http://<server>/ReportServer/` |

---

## ⚙️ Project Setup

### 1️⃣ Clone the Repository
```bash
git clone https://github.com/yourusername/SSRSReportHelper.git
cd SSRSReportHelper
2️⃣ Configure SSRS Settings
In appsettings.json:

json
Copy code
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "AllowedHosts": "*"
}
You don’t hardcode SSRS credentials — they’re entered in the web form.

🧾 Logging (NLog)
All logs are handled by NLog and stored under /logs automatically.

nlog.config

xml
Copy code
<target xsi:type="File" name="mainFile"
        fileName="logs/SSRSUploader.log"
        archiveFileName="logs/archives/SSRSUploader.{#}.log"
        archiveEvery="Day"
        maxArchiveFiles="7"
        createDirs="true"
        layout="${longdate} | ${level:uppercase=true} | ${message} ${exception:format=ToString,StackTrace}" />

<target xsi:type="File" name="failedFile"
        fileName="logs/FailedReports.log"
        createDirs="true"
        layout="${longdate} | ${message}" />

<logger name="*" minlevel="Info" writeTo="mainFile" />
<logger name="FailedReport" minlevel="Info" writeTo="failedFile" />
📂 Logs created:

bash
Copy code
/logs
   SSRSUploader.log
   FailedReports.log
   /archives
🧠 How It Works
🪪 Step 1 — Check Connection
Enter SSRS URL, username, and password.

The app sends a ListChildren SOAP call to validate credentials.

If valid → loads folders and shared data sources.

📤 Step 2 — Upload Reports
Choose target folder.

Select up to 200 .rdl files.

(Optional) Pick a shared data source.

Check “Overwrite existing” if desired.

Click Upload Reports.

Each file is uploaded via:

xml
Copy code
<CreateCatalogItem>
  <ItemType>Report</ItemType>
  <Definition>{Base64EncodedRDL}</Definition>
  <Overwrite>true</Overwrite>
</CreateCatalogItem>
If a data source is selected, the app automatically injects:

xml
Copy code
<DataSourceReference>/Data Sources/MainDS</DataSourceReference>
into the .rdl XML before upload.

📥 Step 3 — Download Reports
Select a folder.

Click Fetch Reports.

The app lists reports in a table (Name + Last Modified).

Select one or more reports and click Download Selected.

A .zip containing .rdl files is generated.

SOAP API used:

xml
Copy code
<GetItemDefinition>
  <ItemPath>/Folder/ReportName</ItemPath>
</GetItemDefinition>
🖥️ UI Overview
Section	Purpose
Index Page	SSRS connection form
Upload Page	Folder selection, file upload, shared DS selection
Download Section	Folder + report selection with Fetch button
Logs	Show upload/download success or errors

🧩 Folder Structure
pgsql
Copy code
SSRSReportHelper/
│
├── Controllers/
│   └── SSRSController.cs
│
├── Models/
│   ├── SSRSConnectionModel.cs
│   └── SSRSItemsModel.cs
│
├── Services/
│   └── SSRSServices.cs
│
├── Views/
│   └── SSRS/
│       ├── Index.cshtml
│       └── Upload.cshtml
│
├── wwwroot/
│   ├── js/
│   │   └── ssrs-reports.js
│   └── css/
│
├── appsettings.json
├── nlog.config
└── README.md
🧩 API Reference
Action	HTTP	Description
/SSRS/CheckConnection	POST	Tests SSRS connection
/SSRS/UploadReports	POST	Uploads RDL reports
/SSRS/DownloadAll	POST	Downloads all reports in folder
/SSRS/DownloadSelected	POST	Downloads selected reports
/SSRS/GetReportsList	POST	Returns reports for folder (JSON)

🧾 Example Log Output
pgsql
Copy code
2025-11-05 12:04:51 | INFO | ✅ Connection Successful
2025-11-05 12:05:14 | INFO | Uploaded report 'SalesSummary' to '/KWT_UAT'
2025-11-05 12:06:03 | INFO | Downloading report: /KWT_UAT/AgeBandReport
2025-11-05 12:06:04 | INFO | ✅ Downloaded 12 reports from '/KWT_UAT'
2025-11-05 12:06:05 | ERROR | ❌ Failed to upload 'RevenueStats': Data source not found
🧩 Troubleshooting
Issue	Cause	Solution
❌ Empty ZIP	selectedReports contained only report names	Fixed by posting full report paths in checkbox value
❌ Undefined names in table	JSON returned camelCase	Use r.name, r.path, r.modifiedDate in JS
⚠️ Folder not showing	Invalid credentials or wrong URL	Test via /ReportServer/ReportService2010.asmx?wsdl
🚫 SSRS 401 Unauthorized	Windows Auth only	Use domain user format: DOMAIN\Username
🪵 Logs not created	Missing folder	NLog createDirs="true" auto-creates /logs

🧩 Future Enhancements
📁 Recursive folder support for nested report download

🧠 Caching to skip re-downloading existing .rdl

🔐 Secure credential encryption (instead of plaintext fields)

📊 Export metadata to Excel (.xlsx using DocumentFormat.OpenXml)

🔄 Bulk update data sources across reports
