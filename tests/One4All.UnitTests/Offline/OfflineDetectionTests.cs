using System.Data.Common;
using System.Net.Sockets;
using FluentAssertions;
using One4All.Application.Offline;
using Xunit;

namespace One4All.UnitTests.Offline;

/// <summary>Covers the connectivity classifier that decides whether a failed sale should be queued offline.</summary>
public sealed class OfflineDetectionTests
{
    private sealed class FakeDbException(string message) : DbException(message);

    [Fact]
    public void A_database_exception_is_a_connectivity_error()
    {
        // Npgsql's exception derives from DbException; a stand-in exercises the same match.
        OfflineDetection.IsConnectivityError(new FakeDbException("connection refused")).Should().BeTrue();
    }

    [Fact]
    public void A_socket_or_timeout_failure_is_a_connectivity_error()
    {
        OfflineDetection.IsConnectivityError(new SocketException()).Should().BeTrue();
        OfflineDetection.IsConnectivityError(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void A_connectivity_error_wrapped_in_an_outer_exception_is_detected()
    {
        Exception wrapped = new InvalidOperationException("posting failed", new FakeDbException("no route to host"));
        OfflineDetection.IsConnectivityError(wrapped).Should().BeTrue();
    }

    [Fact]
    public void A_domain_or_unrelated_exception_is_not_a_connectivity_error()
    {
        OfflineDetection.IsConnectivityError(new InvalidOperationException("period closed")).Should().BeFalse();
        OfflineDetection.IsConnectivityError(new ArgumentException("bad input")).Should().BeFalse();
        OfflineDetection.IsConnectivityError(null).Should().BeFalse();
    }
}
