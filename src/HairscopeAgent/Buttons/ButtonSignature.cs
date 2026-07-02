namespace Hairscope.Agent.Buttons;

/// <summary>
/// Identifies a device's button on the USB bus: the status endpoint and the
/// press payload to match, plus the device identity used for the snap broadcast.
/// </summary>
public sealed record ButtonSignature(
    byte Endpoint,
    byte[] PressPayload,
    string Model,
    string Vid,
    string Pid);
