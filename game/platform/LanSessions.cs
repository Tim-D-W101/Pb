using System;
using System.Collections.Generic;
using System.Linq;
using Pb.Game.Net;
using Pb.Net.Platform;

namespace Pb.Game.Platform;

/// <summary>Finding games on your network (<see cref="LanBrowser"/>'s search), only while a screen lists them.</summary>
public sealed class LanSessions : ISessionBrowser, IDisposable
{
    private readonly int _port;
    private LanBrowser? _browser;
    private IReadOnlyList<FoundGame> _found = Array.Empty<FoundGame>();

    public LanSessions(int discoveryPort)
    {
        _port = discoveryPort;
    }

    public bool Searching
    {
        get => _browser is not null;
        set
        {
            if (value == Searching)
            {
                return;
            }

            if (value)
            {
                _browser = new LanBrowser(_port);
                return;
            }

            _browser!.Dispose();
            _browser = null;
            _found = Array.Empty<FoundGame>();
        }
    }

    public bool CanSearch => _browser?.Bound ?? true;

    public IReadOnlyList<FoundGame> Found => _found;

    public void Poll(double now)
    {
        if (_browser is null)
        {
            return;
        }

        _browser.Poll(now);
        _found = _browser.Games.Select(g => new FoundGame(g.Address, g.Game)).ToArray();
    }

    public void Dispose() => Searching = false;
}
