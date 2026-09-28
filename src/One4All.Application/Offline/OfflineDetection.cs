using System.Data.Common;
using System.Net.Sockets;

namespace One4All.Application.Offline;

/// <summary>
/// Classifies an exception as a loss of connectivity to the central database (as opposed to a domain
/// failure, which comes back as a <c>Result</c> rather than being thrown). The terminal uses this to
/// decide whether to capture a sale into the local offline queue and retry later, versus surfacing a real
/// error the operator must act on. It checks the whole inner-exception chain, and matches by
/// <see cref="DbException"/> (Npgsql's exception derives from it) so no database-provider reference is
/// needed here.
/// </summary>
public static class OfflineDetection
{
    /// <summary>Returns true when the exception indicates the database was unreachable.</summary>
    /// <param name="exception">The exception thrown by a database operation.</param>
    /// <returns>True for a connectivity failure.</returns>
    public static bool IsConnectivityError(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case DbException:      // includes NpgsqlException (a provider connection failure)
                case SocketException:  // the underlying TCP failure
                case TimeoutException: // connection/command timeout
                    return true;
            }
        }

        return false;
    }
}
