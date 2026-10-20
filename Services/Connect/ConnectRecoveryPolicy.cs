namespace OrbitLauncher.Services.Connect;

public sealed class ConnectRecoveryPolicy
{
    private int _failures;
    public bool Observe(bool healthy, bool processExited)
    {
        if (healthy) { _failures = 0; return false; }
        return processExited || ++_failures >= 3;
    }
}
