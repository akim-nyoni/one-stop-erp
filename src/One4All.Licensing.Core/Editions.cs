namespace One4All.Licensing.Core;

/// <summary>Product editions (brief §11.3). An edition is a template that pre-selects a module set.</summary>
public enum LicenceEdition
{
    /// <summary>Entry edition.</summary>
    Standard = 0,

    /// <summary>Mid tier.</summary>
    Professional = 1,

    /// <summary>Full commercial edition.</summary>
    Enterprise = 2,

    /// <summary>Municipality-focused edition.</summary>
    Municipal = 3,

    /// <summary>Education-focused edition.</summary>
    Education = 4,
}

/// <summary>How a licence is acquired and for how long (brief §11.3).</summary>
public enum LicenceType
{
    /// <summary>Perpetual licence (no expiry).</summary>
    Perpetual = 0,

    /// <summary>Time-boxed subscription.</summary>
    Subscription = 1,

    /// <summary>Time-limited trial.</summary>
    Trial = 2,

    /// <summary>Demonstration licence.</summary>
    Demo = 3,

    /// <summary>Evaluation licence.</summary>
    Evaluation = 4,

    /// <summary>Short-term rental.</summary>
    Rental = 5,
}

/// <summary>How a licence is bound to hardware (brief §11.3).</summary>
public enum MachineBindingMode
{
    /// <summary>Not bound to any machine.</summary>
    None = 0,

    /// <summary>Bound to a specific machine.</summary>
    Machine = 1,

    /// <summary>Bound to a server (allowing N fingerprints for failover).</summary>
    Server = 2,

    /// <summary>Bound to a domain.</summary>
    Domain = 3,
}
