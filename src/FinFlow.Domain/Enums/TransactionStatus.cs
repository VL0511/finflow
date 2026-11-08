using System;
using System.Collections.Generic;
using System.Text;

namespace FinFlow.Domain.Enums
{
    public enum TransactionStatus
    {
        Pending = 1,
        Processing = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }
}
