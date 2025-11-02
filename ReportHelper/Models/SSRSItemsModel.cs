namespace ReportHelper.Models
{
    public class SSRSItemsModel
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Folder, Report, DataSource
        public string DataSourcePath { get; set; } = string.Empty; // Folder, Report, DataSource
        public string? CreatedDate { get; set; }
        public string? ModifiedDate { get; set; }
    }
}
