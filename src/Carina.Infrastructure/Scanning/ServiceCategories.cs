using Carina.Broadcast.Descriptors;
using Carina.Domain.Channels;

namespace Carina.Infrastructure.Scanning;

public static class ServiceCategories
{
    public static ServiceCategory Of(ServiceKind kind, bool partiallyReceived)
    {
        if (partiallyReceived)
        {
            return ServiceCategory.OneSeg;
        }

        return kind switch
        {
            ServiceKind.Television or ServiceKind.UltraHighDefinitionTelevision => ServiceCategory.Television,
            ServiceKind.Audio => ServiceCategory.Radio,
            ServiceKind.TemporaryVideo
                or ServiceKind.TemporaryAudio
                or ServiceKind.TemporaryData
                or ServiceKind.Engineering
                or ServiceKind.PromotionVideo
                or ServiceKind.PromotionAudio
                or ServiceKind.PromotionData => ServiceCategory.Temporary,
            ServiceKind.Data
                or ServiceKind.Multimedia
                or ServiceKind.StoredUsingTlv
                or ServiceKind.PreStoredData
                or ServiceKind.StoreOnlyData
                or ServiceKind.BookmarkList
                or ServiceKind.ServerSimultaneous
                or ServiceKind.IndependentFile => ServiceCategory.Data,
            _ => ServiceCategory.Other,
        };
    }
}
