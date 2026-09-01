namespace Carina.Driver.Descrambling;

internal sealed unsafe class CardDescrambler : IDescrambler
{
    private const int Multi2Rounds = 4;

    private const int KeepWhatCannotBeRead = 0;

    private const int LeaveTheCardUnwritten = 0;

    private readonly Lock gate = new();

    private AribStdB25* standard;

    private BCasCard* card;

    private CardDescrambler(AribStdB25* standard, BCasCard* card)
    {
        this.standard = standard;
        this.card = card;
    }

    public static CardDescrambler Open(AribB25Library library)
    {
        ArgumentNullException.ThrowIfNull(library);

        BCasCard* card = library.CreateCard();
        if (card is null)
        {
            throw new DescramblingException(
                "The card interface could not be created, so nothing on this tuner can be unscrambled."
            );
        }

        int opened = card->Init(card);
        if (opened < 0)
        {
            card->Release(card);

            throw new DescramblingException(
                $"No card answered the reader ({Answer(opened)}), so nothing on this tuner can be unscrambled."
            );
        }

        AribStdB25* standard = library.CreateStandard();
        if (standard is null)
        {
            card->Release(card);

            throw new DescramblingException(
                "The descrambler could not be created, so nothing on this tuner can be unscrambled."
            );
        }

        try
        {
            Settle(standard, card);
        }
        catch
        {
            standard->Release(standard);
            card->Release(card);

            throw;
        }

        return new CardDescrambler(standard, card);
    }

    private static void Settle(AribStdB25* standard, BCasCard* card)
    {
        Insist(standard->SetMulti2Round(standard, Multi2Rounds), "the round count");
        Insist(standard->SetStrip(standard, KeepWhatCannotBeRead), "keeping every packet");
        Insist(
            standard->SetEntitlementManagementProcessing(standard, LeaveTheCardUnwritten),
            "leaving the card unwritten"
        );
        Insist(standard->SetCard(standard, card), "the card");
    }

    private static void Insist(int answer, string what)
    {
        if (answer < 0)
        {
            throw new DescramblingException(
                $"The descrambler refused {what} ({Answer(answer)}), so nothing on this tuner can be unscrambled."
            );
        }
    }

    public byte[] Descramble(ReadOnlySpan<byte> stream)
    {
        if (stream.Length is 0)
        {
            return [];
        }

        lock (gate)
        {
            AribStdB25* open = Alive();

            fixed (byte* input = stream)
            {
                AribBuffer taking = new() { Data = input, Size = (uint)stream.Length };

                int taken = open->Put(open, &taking);
                if (taken < 0)
                {
                    throw new DescramblingException(
                        $"The descrambler would not take {stream.Length} bytes of the stream ({Answer(taken)})."
                    );
                }
            }

            return Collect(open);
        }
    }

    public byte[] WhatItCouldNotRead()
    {
        lock (gate)
        {
            if (standard is null)
            {
                return [];
            }

            AribStdB25* open = standard;
            AribBuffer giving = default;

            return open->Withdraw(open, &giving) < 0 || giving.Data is null || giving.Size is 0
                ? []
                : new ReadOnlySpan<byte>(giving.Data, (int)giving.Size).ToArray();
        }
    }

    private static byte[] Collect(AribStdB25* open)
    {
        AribBuffer giving = default;

        int given = open->Get(open, &giving);
        if (given < 0)
        {
            throw new DescramblingException(
                $"The descrambler would not hand back what it had unscrambled ({Answer(given)})."
            );
        }

        return giving.Data is null || giving.Size is 0
            ? []
            : new ReadOnlySpan<byte>(giving.Data, (int)giving.Size).ToArray();
    }

    private AribStdB25* Alive() =>
        standard is null
            ? throw new ObjectDisposedException(nameof(CardDescrambler))
            : standard;

    public void Dispose()
    {
        lock (gate)
        {
            if (standard is not null)
            {
                standard->Release(standard);
                standard = null;
            }

            if (card is not null)
            {
                card->Release(card);
                card = null;
            }
        }
    }

    private static string Answer(int code) => $"code {code}";
}
