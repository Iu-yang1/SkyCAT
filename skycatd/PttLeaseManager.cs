namespace skycatd
{
  /// <summary>
  /// Exclusive transmit-actuation lease shared by the main CAT and WSJT-X
  /// endpoints. It coordinates both legacy PTT and the constrained IC-9700
  /// CW text keyer so two clients can never independently start transmit
  /// actions through the same radio.
  ///
  /// The historical class name is retained for source compatibility.
  /// </summary>
  public sealed class PttLeaseManager
  {
    private enum LeaseKind
    {
      Ptt,
      Cw
    }

    private readonly Func<string, string> Forward;
    private readonly object Sync = new();
    private object? Owner;
    private LeaseKind? Kind;
    private bool MayBeActive;
    private bool Orphaned;

    public PttLeaseManager(
      Func<string, string> forward) =>
      Forward = forward;

    public string Execute(
      object client,
      string command)
    {
      var args =
        command.Split(
          ' ',
          StringSplitOptions.RemoveEmptyEntries);

      if (args.Length == 2 &&
          args[0] == "T")
      {
        if (args[1] == "1")
          return KeyPtt(client);
        if (args[1] == "0")
          return UnkeyPtt(client);
      }

      if (command.StartsWith(
            "U CW_SEND ",
            StringComparison.Ordinal))
        return SendCw(client, command);

      if (string.Equals(
            command,
            "U CW_ABORT",
            StringComparison.Ordinal))
        return AbortCw(command);

      if (args.Length >= 2 &&
          args[0] == "U" &&
          args[1] == "CW_SPEED")
        return SetCwSpeed(
          client,
          command);

      return Forward(command);
    }

    private string KeyPtt(
      object client)
    {
      lock (Sync)
      {
        RecoverOrphanedLease();

        if (Owner != null)
          return
            ReferenceEquals(
              Owner,
              client) &&
            Kind == LeaseKind.Ptt &&
            MayBeActive
              ? "RPRT 0"
              : "RPRT -6";

        Reserve(
          client,
          LeaseKind.Ptt);

        try
        {
          string reply =
            Forward("T 1");

          if (reply == "RPRT 0")
            return reply;

          if (IsDefiniteRejection(reply))
            Clear();
          else
            TryFailSafeRelease();

          return reply;
        }
        catch
        {
          TryFailSafeRelease();
          throw;
        }
      }
    }

    private string UnkeyPtt(
      object client)
    {
      lock (Sync)
      {
        if (!ReferenceEquals(
              Owner,
              client) ||
            Kind != LeaseKind.Ptt)
          return "RPRT 0";

        string result =
          Forward("T 0");

        if (result == "RPRT 0")
          Clear();

        return result;
      }
    }

    private string SendCw(
      object client,
      string command)
    {
      lock (Sync)
      {
        RecoverOrphanedLease();

        bool continuing =
          Owner != null &&
          ReferenceEquals(
            Owner,
            client) &&
          Kind == LeaseKind.Cw &&
          MayBeActive;

        if (Owner != null &&
            !continuing)
          return "RPRT -6";

        if (Owner == null)
          Reserve(
            client,
            LeaseKind.Cw);

        try
        {
          string reply =
            Forward(command);

          if (reply == "RPRT 0")
            return reply;

          if (IsDefiniteRejection(reply))
          {
            // A definite rejection of the first frame cannot have started a
            // CW queue. If this owner was appending a later frame, preserve
            // the existing lease because earlier ACKed text may still be
            // transmitting.
            if (!continuing)
              Clear();

            return reply;
          }

          // Timeout/I/O/internal failures are ambiguous: the radio may have
          // accepted command 17 before the response was lost.
          TryFailSafeRelease();
          return reply;
        }
        catch
        {
          TryFailSafeRelease();
          throw;
        }
      }
    }

    private string AbortCw(
      string command)
    {
      lock (Sync)
      {
        string result =
          Forward(command);

        if (result == "RPRT 0" &&
            Kind == LeaseKind.Cw)
          Clear();

        return result;
      }
    }

    private string SetCwSpeed(
      object client,
      string command)
    {
      lock (Sync)
      {
        RecoverOrphanedLease();

        if (Owner != null &&
            (!ReferenceEquals(
                Owner,
                client) ||
             Kind != LeaseKind.Cw))
          return "RPRT -6";

        return Forward(command);
      }
    }

    public void Release(
      object client)
    {
      lock (Sync)
      {
        if (!ReferenceEquals(
              Owner,
              client))
          return;

        TryFailSafeRelease();

        if (Owner != null)
          Orphaned = true;
      }
    }

    private void RecoverOrphanedLease()
    {
      if (Owner != null &&
          Orphaned)
        TryFailSafeRelease();
    }

    private void TryFailSafeRelease()
    {
      if (Owner == null ||
          !MayBeActive ||
          Kind == null)
        return;

      try
      {
        string safeCommand =
          Kind == LeaseKind.Cw
            ? "U CW_ABORT"
            : "T 0";

        if (Forward(safeCommand) !=
            "RPRT 0")
          return;

        Clear();
      }
      catch
      {
        // Keep the uncertain lease. No second client may obtain a transmit
        // action until the transport recovers and the safe release succeeds.
      }
    }

    private void Reserve(
      object client,
      LeaseKind kind)
    {
      Owner = client;
      Kind = kind;
      MayBeActive = true;
      Orphaned = false;
    }

    private void Clear()
    {
      Owner = null;
      Kind = null;
      MayBeActive = false;
      Orphaned = false;
    }

    private static bool IsDefiniteRejection(
      string reply) =>
      reply is
        "RPRT -9" or
        "RPRT -11" or
        "RPRT -1";

    /// <summary>
    /// Called after serial transport recovery. A possibly active PTT or CW
    /// keyer lease is fail-safe released before another client can transmit.
    /// </summary>
    public void RetryUncertainRelease()
    {
      lock (Sync)
      {
        if (Owner != null &&
            MayBeActive)
          TryFailSafeRelease();
      }
    }
  }
}
