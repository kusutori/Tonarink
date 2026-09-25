namespace LocalSendDotNet;

/// <summary>Base exception for LocalSend-specific failures.</summary>
public class LocalSendException : Exception
{
    /// <summary>Creates an exception with a diagnostic message.</summary>
    public LocalSendException(string message) : base(message) { }
    /// <summary>Creates an exception with a diagnostic message and underlying cause.</summary>
    public LocalSendException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Indicates that a peer certificate or advertised fingerprint failed validation.</summary>
public sealed class PeerIdentityException : LocalSendException
{
    /// <summary>Creates a peer identity exception.</summary>
    public PeerIdentityException(string message) : base(message) { }
    /// <summary>Creates a peer identity exception with its underlying cause.</summary>
    public PeerIdentityException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Indicates that the persistent local certificate identity is incomplete or corrupt.</summary>
public sealed class IdentityLoadException : LocalSendException
{
    /// <summary>Creates an identity load exception.</summary>
    public IdentityLoadException(string message) : base(message) { }
    /// <summary>Creates an identity load exception with its underlying cause.</summary>
    public IdentityLoadException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Indicates that the configured TCP listening port is unavailable.</summary>
public sealed class PortUnavailableException : LocalSendException
{
    /// <summary>Creates a port availability exception.</summary>
    /// <param name="port">The unavailable TCP port.</param>
    /// <param name="innerException">The underlying bind failure.</param>
    public PortUnavailableException(int port, Exception innerException)
        : base($"TCP port {port} is unavailable. Choose another LocalSendOptions.Port or stop the process using it.", innerException) => Port = port;

    /// <summary>Gets the unavailable port.</summary>
    public int Port { get; }
}

/// <summary>Indicates that IPv4 multicast discovery could not bind or join an interface.</summary>
public sealed class DiscoveryUnavailableException : LocalSendException
{
    /// <summary>Creates a discovery availability exception.</summary>
    public DiscoveryUnavailableException(string message) : base(message) { }
    /// <summary>Creates a discovery availability exception with its underlying cause.</summary>
    public DiscoveryUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
