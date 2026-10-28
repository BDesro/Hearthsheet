namespace Hearthsheet.Web;

public partial class App
{
#if DEBUG
    // TEMPORARY Task 8 dev page, removed in Task 11.
    private static readonly Type? DevPage = typeof(Dev.DevStorage);
#else
    private static readonly Type? DevPage = null;
#endif
}
