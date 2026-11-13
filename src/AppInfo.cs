namespace IslandShot
{
    // 版本号集中在这里：标题、托盘、发布包名都用它，避免各处对不上。
    internal static class AppInfo
    {
        internal const string ProductName = "万象岛 Pro";
        internal const string ShortName = "万象岛";
        internal const string Version = "3.20";

        internal static string Title
        {
            get { return ProductName; }
        }

        internal static string TitleWithVersion
        {
            get { return ProductName + " v" + Version; }
        }

        internal static string PackageName
        {
            get { return "万象岛-Pro-" + Version; }
        }
    }
}
