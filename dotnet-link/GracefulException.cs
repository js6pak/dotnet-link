// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

namespace DotNetLink;

internal sealed class GracefulException : Exception
{
    public GracefulException()
    {
    }

    public GracefulException(string message) : base(message)
    {
    }

    public GracefulException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
