namespace Hairscope.Agent.Buttons;

/// <summary>Watches a device for hardware snapshot-button presses.</summary>
public interface IButtonWatcher : IDisposable
{
    /// <summary>Raised once per physical button press, with the matched device's signature.</summary>
    event Action<ButtonSignature>? ButtonPressed;

    /// <summary>Begin watching. Returns false if it could not start.</summary>
    bool Start();

    /// <summary>Stop watching.</summary>
    void Stop();
}
