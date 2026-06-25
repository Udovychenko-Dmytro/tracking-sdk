namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>Toggleable <see cref="IConnectivity"/> for deterministic offline/online tests.</summary>
    internal sealed class FakeConnectivity : IConnectivity
    {
        public bool IsOnline { get; set; } = true;
    }
}
