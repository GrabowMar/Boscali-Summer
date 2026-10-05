using System;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    internal enum LadderStep : byte { Who, Do, Where }

    /// <summary>What a digit did: nothing, moved (a step forward, a page, or back), left the ladder, or the order is complete.</summary>
    internal enum LadderKind : byte { None, Moved, Close, Send }

    /// <summary>The Call Ladder's navigation (user ruling 2026-10-04): WHO -> DO -> WHERE, digits 1-9 pick, 0 goes back. An order that
    /// needs nothing more sends at DO; a page holds <c>perPage</c> choices and, when there are more pages, the next digit is MORE.
    /// Pure: the caller supplies how many choices the current step has, and reads the picks back as absolute indexes.</summary>
    internal sealed class LadderState
    {
        public const int MaxPerPage = 8, SinglePage = 9;

        public LadderStep Step { get; private set; }
        public int Who { get; private set; } = -1;
        public int Do { get; private set; } = -1;
        public int Where { get; private set; } = -1;
        private readonly int[] pages = new int[3];

        public int Page => pages[(int)Step];

        public void Reset()
        {
            Step = LadderStep.Who;
            Who = Do = Where = -1;
            Array.Clear(pages, 0, pages.Length);
        }

        /// <summary>Starts at WHERE for order <paramref name="doIndex"/> to the whole wing (WHO 0), as a chord on a letter does.</summary>
        public void OpenAt(int doIndex, int perPageOfDo)
        {
            Reset();
            Who = 0;
            Do = doIndex;
            Step = LadderStep.Where;
            pages[(int)LadderStep.Do] = perPageOfDo > 0 ? doIndex / perPageOfDo : 0;
        }

        /// <summary>Choices per page for a list of <paramref name="total"/>: all on one page up to nine, else eight and a MORE key.</summary>
        public static int AutoPerPage(int total) => total <= SinglePage ? SinglePage : MaxPerPage;

        public static int PageCount(int total, int perPage) => total <= 0 || perPage <= 0 ? 1 : (total + perPage - 1) / perPage;

        /// <summary>How many choices page <paramref name="page"/> shows (digits 1..count).</summary>
        public static int OnPage(int total, int perPage, int page)
        {
            int left = total - page * perPage;
            return left < 0 ? 0 : left < perPage ? left : perPage;
        }

        /// <summary>The digit that turns the page (0 when there is one page).</summary>
        public static int MoreDigit(int total, int perPage) => PageCount(total, perPage) > 1 ? perPage + 1 : 0;

        /// <summary>Applies a digit to the current step's list. <paramref name="needsWhere"/> says whether the chosen DO choice
        /// asks for a WHERE; null means none does.</summary>
        public LadderKind Press(int digit, int total, int perPage, Func<int, bool> needsWhere)
        {
            if (digit < 0 || digit > 9) return LadderKind.None;
            if (digit == 0) return Back();
            int step = (int)Step;
            if (digit == MoreDigit(total, perPage))
            {
                pages[step] = (pages[step] + 1) % PageCount(total, perPage);
                return LadderKind.Moved;
            }
            if (digit > OnPage(total, perPage, pages[step])) return LadderKind.None;
            int index = pages[step] * perPage + digit - 1;
            switch (Step)
            {
                case LadderStep.Who:
                    Who = index;
                    Step = LadderStep.Do;
                    return LadderKind.Moved;
                case LadderStep.Do:
                    Do = index;
                    if (needsWhere != null && needsWhere(index))
                    {
                        Step = LadderStep.Where;
                        pages[(int)LadderStep.Where] = 0;
                        return LadderKind.Moved;
                    }
                    return LadderKind.Send;
                default:
                    Where = index;
                    return LadderKind.Send;
            }
        }

        private LadderKind Back()
        {
            switch (Step)
            {
                case LadderStep.Where:
                    Where = -1;
                    Step = LadderStep.Do;
                    return LadderKind.Moved;
                case LadderStep.Do:
                    Do = -1;
                    Step = LadderStep.Who;
                    return LadderKind.Moved;
                default:
                    Who = -1;
                    return LadderKind.Close;
            }
        }
    }
}
