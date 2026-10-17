namespace One4All.Core.Trading;

/// <summary>The kind of open-item document in a customer/supplier ledger (brief §4.5/§4.6).</summary>
public enum OpenItemType
{
    /// <summary>An invoice - increases what the trader owes (AR) / what we owe (AP).</summary>
    Invoice = 1,

    /// <summary>A credit note - decreases the balance.</summary>
    CreditNote = 2,

    /// <summary>A debit note - increases the balance.</summary>
    DebitNote = 3,

    /// <summary>A receipt (AR) - money received, decreases the debtor balance.</summary>
    Receipt = 4,

    /// <summary>A payment (AP) - money paid, decreases the creditor balance.</summary>
    Payment = 5,

    /// <summary>A ledger journal adjustment.</summary>
    Journal = 6,

    /// <summary>A write-off.</summary>
    WriteOff = 7,

    /// <summary>Interest / penalty charge.</summary>
    Interest = 8,
}

/// <summary>How ageing is measured (brief §4.5).</summary>
public enum AgeingBasis
{
    /// <summary>Age from the document date.</summary>
    InvoiceDate = 0,

    /// <summary>Age from the due date.</summary>
    DueDate = 1,
}

/// <summary>The statement style for a trader (brief §4.5).</summary>
public enum StatementType
{
    /// <summary>Open-item statement: every outstanding document.</summary>
    OpenItem = 0,

    /// <summary>Balance-forward statement: opening balance + period movements.</summary>
    BalanceForward = 1,
}

/// <summary>
/// Whether an open item is a debit (increases the trader balance in the ledger's natural direction) or a
/// credit (decreases it). For AR, invoices are debits and receipts/credit notes are credits; allocation
/// matches debits against credits.
/// </summary>
public enum LedgerSign
{
    /// <summary>A debit item (AR invoice, AP debit - increases the outstanding balance).</summary>
    Debit = 1,

    /// <summary>A credit item (AR receipt/credit note - decreases the outstanding balance).</summary>
    Credit = 2,
}

/// <summary>Helpers for open-item types.</summary>
public static class OpenItemTypeExtensions
{
    /// <summary>Returns the natural ledger sign of an item type for the accounts-receivable ledger.</summary>
    /// <param name="type">The item type.</param>
    /// <returns>Debit for invoices/debit notes/interest; credit for receipts/credit notes/write-offs.</returns>
    public static LedgerSign ArSign(this OpenItemType type) => type switch
    {
        OpenItemType.Invoice or OpenItemType.DebitNote or OpenItemType.Interest => LedgerSign.Debit,
        _ => LedgerSign.Credit,
    };

    /// <summary>Returns the natural ledger sign of an item type for the accounts-payable ledger.</summary>
    /// <param name="type">The item type.</param>
    /// <returns>Credit for invoices/debit notes; debit for payments/credit notes.</returns>
    public static LedgerSign ApSign(this OpenItemType type) => type switch
    {
        OpenItemType.Invoice or OpenItemType.Interest => LedgerSign.Credit,
        OpenItemType.DebitNote => LedgerSign.Debit,
        OpenItemType.Payment or OpenItemType.CreditNote or OpenItemType.WriteOff => LedgerSign.Debit,
        _ => LedgerSign.Credit,
    };
}
