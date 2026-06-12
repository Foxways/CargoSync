namespace OrganizationImportTool.Auth
{
    /// <summary>An authenticated application user.</summary>
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
    }
}
