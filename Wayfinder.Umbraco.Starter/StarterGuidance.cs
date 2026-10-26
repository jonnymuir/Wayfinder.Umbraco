namespace Wayfinder.Umbraco.Starter;

/// <summary>What a connecting MCP client is told about this kind of site, on top of Wayfinder's own guidance.</summary>
public static class StarterGuidance
{
    public const string McpInstructions =
        "This is a Wayfinder starter site, for trying Wayfinder out. " +
        "A saved blueprint is live straight away at /services/{definitionKey}, and the site's home page lists it. " +
        "The queue of a blueprint's initial stage belongs to the public: anyone can start there without signing in, and sees only their own requests. " +
        "Every other queue is staff work, done by the demo caseworker on the site's work queue page (/work-queue). " +
        "So give each service one queue for the person applying, put the initial stage in it, and put the staff stages in other queues. " +
        "Once a blueprint is saved, tell the person to open /services/{definitionKey} to try it as a member of the public, and /work-queue to work it as the caseworker.";
}
