namespace Wayfinder.Umbraco.Starter;

public static class StarterRoutes
{
    public const string Services = "/services";

    public const string WorkQueue = "/work-queue";

    public const string SignIn = "/starter/sign-in";

    public const string SignOut = "/starter/sign-out";

    public static string Service(string definitionKey) => $"{Services}/{Uri.EscapeDataString(definitionKey)}";
}
