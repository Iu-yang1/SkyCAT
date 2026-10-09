namespace skycatd
{
  /// <summary>
  /// Exclusive CAT PTT lease shared by the main CAT and WSJT-X endpoints.
  /// The sender is called under the same serialization lock used for all CI-V.
  /// A failed/ambiguous ON never grants PTT to a second client until an OFF
  /// has positively succeeded. Front-panel PTT is never acquired implicitly.
  /// </summary>
  public sealed class PttLeaseManager
  {
    private readonly Func<string, string> Forward;
    private readonly object Sync = new();
    private object? Owner;
    private bool MayBeKeyed;
    private bool Orphaned;

    public PttLeaseManager(Func<string, string> forward) =>
      Forward = forward;

    public string Execute(object client, string command)
    {
      var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (args.Length != 2 || args[0] != "T")
        return Forward(command);

      if (args[1] == "1") return Key(client);
      if (args[1] == "0") return Unkey(client);
      return Forward(command);
    }

    private string Key(object client)
    {
      lock (Sync)
      {
        // After a serial outage a disconnected client may retain an
        // uncertain lease. First prove the transmitter is OFF before
        // transferring ownership; a failed recovery remains fail-closed.
        if (Owner != null && Orphaned)
          TryFailSafeUnkey();
        if (Owner != null)
          return ReferenceEquals(Owner, client) && MayBeKeyed
            ? "RPRT 0"
            : "RPRT -6"; // already owned by another client or uncertain

        // Reserve ownership BEFORE writing: a timed-out ON may have keyed
        // the radio even if SkyCAT never received the acknowledgement.
        Owner = client;
        MayBeKeyed = true;
        Orphaned = false;
        try
        {
          string reply = Forward("T 1");
          if (reply == "RPRT 0") return reply;

          // A definite radio rejection cannot key it. For an ambiguous
          // timeout/I/O failure, attempt an immediate fail-safe OFF.
          if (reply == "RPRT -9")
          {
            Owner = null;
            MayBeKeyed = false;
            Orphaned = false;
          }
          else
            TryFailSafeUnkey();
          return reply;
        }
        catch
        {
          TryFailSafeUnkey();
          throw;
        }
      }
    }

    private string Unkey(object client)
    {
      lock (Sync)
      {
        if (!ReferenceEquals(Owner, client))
          return "RPRT 0"; // never unkey another client's transmission

        string result = Forward("T 0");
        if (result == "RPRT 0")
        {
          Owner = null;
          MayBeKeyed = false;
          Orphaned = false;
        }
        return result;
      }
    }

    public void Release(object client)
    {
      lock (Sync)
      {
        if (!ReferenceEquals(Owner, client)) return;
        TryFailSafeUnkey();
        if (Owner != null)
          Orphaned = true;
      }
    }

    private void TryFailSafeUnkey()
    {
      try
      {
        if (Forward("T 0") != "RPRT 0")
          return;
        Owner = null;
        MayBeKeyed = false;
      }
      catch
      {
        // Keep the uncertain lease. A second client may NOT key until the
        // transport comes back and the shutdown/reconnect fail-safe unkeys.
      }
    }

    /// <summary>Called when serial transport is recovered after an uncertain key.</summary>
    public void RetryUncertainRelease()
    {
      lock (Sync)
      {
        if (Owner != null && MayBeKeyed)
          TryFailSafeUnkey();
      }
    }
  }
}
