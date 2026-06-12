namespace OrganizationImportTool.Eadaptor
{
    /// <summary>Pairs one source row's submission with the eAdaptor response, for the preview.</summary>
    public class OrgSendOutcome
    {
        public int RowNumber { get; set; }
        public string SentCode { get; set; } = string.Empty;
        public string SentXml { get; set; } = string.Empty;
        public EadaptorResponse Response { get; set; } = new EadaptorResponse();
    }
}
