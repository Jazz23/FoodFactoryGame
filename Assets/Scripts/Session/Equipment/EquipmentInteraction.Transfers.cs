// Optimistic previews of this client's own goods transfers, so a drop or shift+click shows at once instead of after the
// server's answer and the next replicated baseline. Presentation only: the server still checks and applies every transfer, and
// the authoritative baseline always wins. A preview is dropped when the server rejects its request (the screen shows the goods
// back where they were), once a baseline at or past the accepted revision arrives (the baseline then shows the move itself), when
// the subscription changes, or after PreviewTimeoutSeconds without an answer. Nothing here ever creates, removes or moves goods.
using System;
using System.Collections.Generic;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    public sealed partial class EquipmentInteraction
    {
        private const float PreviewTimeoutSeconds = 10f;

        private sealed class TransferPreview
        {
            public string RequestId;
            public string LotId;
            public string SourceId;
            public string DestinationId;
            public string ItemId;
            public bool Spoiled;
            public int Quantity;
            // World revision the accepted transfer produced; -1 until the server accepts it.
            public long AcceptedRevision = -1;
            public float Started;
        }

        private readonly List<TransferPreview> _previews = new();

        // True while any of this client's transfers is still shown ahead of the authoritative baseline.
        public bool HasTransferPreviews => _previews.Count > 0;

        private void AddPreview(string requestId, GoodsLot lot, string destinationId, int quantity) => _previews.Add(new TransferPreview
        {
            RequestId = requestId, LotId = lot.Id, SourceId = lot.LocationId, DestinationId = destinationId, ItemId = lot.ItemId,
            Spoiled = lot.Spoiled, Quantity = quantity, Started = Time.unscaledTime
        });

        // A rejection reverts the preview; an acceptance keeps it until a baseline shows the move.
        private void OnTransferResult(GoodsOutcome outcome)
        {
            var index = _previews.FindIndex(x => x.RequestId == outcome.RequestId);
            if (index < 0) return;
            if (!outcome.Accepted) _previews.RemoveAt(index);
            else _previews[index].AcceptedRevision = outcome.Revision;
        }

        private void ExpireTransferPreviews()
        {
            if (_previews.Count == 0) return;
            var site = session.ClientSite;
            var now = Time.unscaledTime;
            _previews.RemoveAll(x => !Applies(x, site) || now - x.Started > PreviewTimeoutSeconds);
        }

        private void ClearTransferPreviews() => _previews.Clear();

        // A preview still counts against a baseline that predates its accepted revision.
        private static bool Applies(TransferPreview preview, GoodsSnapshot site) =>
            site != null && (preview.AcceptedRevision < 0 || site.Revision < preview.AcceptedRevision);

        // The goods in one location as this client shows them: the baseline's lots with its own pending transfers applied, one
        // aggregate lot per (item, spoiled) stack (Id empty; never sent to the server). With no previews it is the baseline's
        // stacks.
        public List<GoodsLot> PreviewLots(GoodsSnapshot site, string locationId)
        {
            var result = new List<GoodsLot>();
            if (site == null || locationId == null) return result;
            var totals = new Dictionary<(string ItemId, bool Spoiled), long>();
            var order = new List<(string ItemId, bool Spoiled)>();
            void Add(string itemId, bool spoiled, long quantity)
            {
                var key = (itemId, spoiled);
                if (!totals.ContainsKey(key))
                {
                    totals[key] = 0;
                    order.Add(key);
                }
                totals[key] += quantity;
            }
            foreach (var lot in site.Lots)
                if (lot.LocationId == locationId) Add(lot.ItemId, lot.Spoiled, lot.Quantity);
            foreach (var preview in _previews)
            {
                if (!Applies(preview, site)) continue;
                if (preview.SourceId == locationId) Add(preview.ItemId, preview.Spoiled, -preview.Quantity);
                if (preview.DestinationId == locationId) Add(preview.ItemId, preview.Spoiled, preview.Quantity);
            }
            foreach (var key in order)
            {
                var quantity = totals[key];
                if (quantity <= 0) continue;
                result.Add(new GoodsLot
                {
                    Id = "", LocationId = locationId, ItemId = key.ItemId, Spoiled = key.Spoiled, Quantity = (int)Math.Min(int.MaxValue, quantity)
                });
            }
            return result;
        }

        // Units of a lot already promised to pending transfers, so a quick second click never asks for them again.
        private long PreviewedOut(string lotId, GoodsSnapshot site)
        {
            var units = 0L;
            foreach (var preview in _previews)
                if (preview.LotId == lotId && Applies(preview, site)) units += preview.Quantity;
            return units;
        }
    }
}
