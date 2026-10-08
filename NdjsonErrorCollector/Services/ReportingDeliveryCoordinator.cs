using System;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class ReportingDeliveryCoordinator
    {
        public void CompleteSuccessfulDelivery(DeduplicationState state, DateTime deliveredThroughUtc)
        {
            foreach (var group in state.Groups.Values)
            {
                group.LastSuccessfulSendUtc = deliveredThroughUtc.ToString("O");
                group.PendingThroughUtc = null;
                group.PendingRecords.Clear();
            }

            foreach (var warningKey in state.PendingWarnings.Keys)
            {
                state.NotifiedWarningKeys.Add(warningKey);
            }

            state.PendingWarnings.Clear();
        }
    }
}
