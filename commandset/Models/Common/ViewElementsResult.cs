namespace RevitMCPCommandSet.Models.Common
{
    public class ViewElementsResult
    {
        public long ViewId { get; set; }
        public string ViewName { get; set; }
        public int TotalElementsInView { get; set; }
        public int FilteredElementCount { get; set; }
        public bool Truncated { get; set; }
        public List<ElementInfo> Elements { get; set; } = new List<ElementInfo>();
    }
}
