using System;
using NBitcoin;

namespace Ztm.Data.Entity.Contexts.Main
{
    public class Input : IComparable<Input>
    {
        public uint256 TransactionHash { get; set; }
        public long Index { get; set; }
        public uint256 OutputHash { get; set; }
        public long OutputIndex { get; set; }
        public Script Script { get; set; }
        public long Sequence { get; set; }

        public Transaction Transaction { get; set; }

        public int CompareTo(Input other)
        {
            if (other == null)
            {
                return 1;
            }

            if (TransactionHash < other.TransactionHash)
            {
                return -1;
            }
            else if (TransactionHash > other.TransactionHash)
            {
                return 1;
            }

            if (Index < other.Index)
            {
                return -1;
            }
            else if (Index > other.Index)
            {
                return 1;
            }

            return 0;
        }

        public override bool Equals(Object other)
        {
            if (other == null)
            {
                return false;
            }

            if (GetType() != other.GetType())
            {
                return false;
            }

            return CompareTo((Input)other) == 0;
        }

        public override int GetHashCode()
        {
            int hash = 0;

            hash ^= (TransactionHash != null) ? TransactionHash.GetHashCode() : 0;
            hash ^= (int)Index;
            hash ^= (OutputHash != null) ? OutputHash.GetHashCode() : 0;
            hash ^= (int)OutputIndex;
            hash ^= (Script != null) ? Script.GetHashCode() : 0;
            hash ^= (int)Sequence;
            hash ^= (Transaction != null) ? Transaction.GetHashCode() : 0;

            return hash;
        }
    }
}
