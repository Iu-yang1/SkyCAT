namespace skycatd
{
  /// <summary>
  /// Exclusive lease for the IC-9700 internal CW message sender.
  ///
  /// Command 17 has no useful "message still busy" query for this protocol,
  /// so ownership is deliberately conservative: a successful SEND keeps the
  /// lease until the same client ABORTs or disconnects. An ambiguous SEND
  /// failure immediately attempts command 17/FF. If that cannot be confirmed,
  /// the lease remains orphaned and blocks new senders until recovery.
  /// </summary>
  public sealed class CwMessageLeaseManager
  {
    private const string AbortCommand =
      "U CW_ABORT";

    private readonly Func<string, string> Forward;
    private readonly object Sync = new();

    private object? Owner;
    private bool MayBeSending;
    private bool Orphaned;

    public CwMessageLeaseManager(
      Func<string, string> forward)
    {
      Forward =
        forward ??
        throw new ArgumentNullException(
          nameof(forward));
    }

    public string Execute(
      object client,
      string command)
    {
      if (IsSend(command))
        return Send(client, command);

      if (IsAbort(command))
        return Abort(client);

      return Forward(command);
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

        TryFailSafeAbort();

        if (Owner != null)
          Orphaned = true;
      }
    }

    public void RetryUncertainRelease()
    {
      lock (Sync)
      {
        if (Owner != null &&
            MayBeSending)
          TryFailSafeAbort();
      }
    }

    internal bool HasOwner
    {
      get
      {
        lock (Sync)
          return Owner != null;
      }
    }

    internal bool IsOrphaned
    {
      get
      {
        lock (Sync)
          return Orphaned;
      }
    }

    private string Send(
      object client,
      string command)
    {
      lock (Sync)
      {
        if (Owner != null &&
            Orphaned)
          TryFailSafeAbort();

        if (Owner != null &&
            !ReferenceEquals(
              Owner,
              client))
          return "RPRT -6";

        // Reserve ownership before sending. A timeout can happen after the
        // radio has already started its internal keyer.
        Owner = client;
        MayBeSending = true;
        Orphaned = false;

        try
        {
          string reply =
            Forward(command);

          if (reply == "RPRT 0")
            return reply;

          if (IsDefiniteNoStart(reply))
          {
            ClearOwner();
            return reply;
          }

          TryFailSafeAbort();
          return reply;
        }
        catch
        {
          TryFailSafeAbort();
          throw;
        }
      }
    }

    private string Abort(
      object client)
    {
      lock (Sync)
      {
        // Never allow one client to stop another client's keyer.
        if (!ReferenceEquals(
              Owner,
              client))
          return "RPRT 0";

        string reply =
          Forward(AbortCommand);

        if (reply == "RPRT 0")
          ClearOwner();

        return reply;
      }
    }

    private void TryFailSafeAbort()
    {
      try
      {
        if (Forward(AbortCommand) !=
            "RPRT 0")
          return;

        ClearOwner();
      }
      catch
      {
        // Keep the uncertain owner. A new client may not start CW until the
        // serial transport recovers and this abort can be confirmed.
      }
    }

    private void ClearOwner()
    {
      Owner = null;
      MayBeSending = false;
      Orphaned = false;
    }

    private static bool IsDefiniteNoStart(
      string reply) =>
      reply is
        "RPRT -1" or
        "RPRT -9" or
        "RPRT -11";

    private static bool IsSend(
      string command)
    {
      string[] args =
        command.Split(
          ' ',
          StringSplitOptions.RemoveEmptyEntries);

      return args.Length == 3 &&
             args[0] == "U" &&
             args[1] == "CW_SEND64";
    }

    private static bool IsAbort(
      string command)
    {
      string[] args =
        command.Split(
          ' ',
          StringSplitOptions.RemoveEmptyEntries);

      return args.Length == 2 &&
             args[0] == "U" &&
             args[1] == "CW_ABORT";
    }
  }

  public sealed class CwMessageCommandSession
  {
    private readonly Func<string, string> Forward;
    private readonly CwMessageLeaseManager SharedLease;

    public CwMessageCommandSession(
      Func<string, string> forward,
      CwMessageLeaseManager sharedLease)
    {
      Forward =
        forward ??
        throw new ArgumentNullException(
          nameof(forward));
      SharedLease =
        sharedLease ??
        throw new ArgumentNullException(
          nameof(sharedLease));
    }

    public string Execute(
      string command)
    {
      string[] args =
        command.Split(
          ' ',
          StringSplitOptions.RemoveEmptyEntries);

      bool isCwWrite =
        args.Length >= 2 &&
        args[0] == "U" &&
        (args[1] == "CW_SEND64" ||
         args[1] == "CW_ABORT");

      return isCwWrite
        ? SharedLease.Execute(
            this,
            command)
        : Forward(command);
    }

    public void EnsureCwAbort() =>
      SharedLease.Release(this);
  }
}
