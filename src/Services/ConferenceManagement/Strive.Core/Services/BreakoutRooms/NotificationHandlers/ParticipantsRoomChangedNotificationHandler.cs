using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Strive.Core.Services.Permissions.Requests;
using Strive.Core.Services.Rooms.Notifications;

namespace Strive.Core.Services.BreakoutRooms.NotificationHandlers
{
    public class ParticipantsRoomChangedNotificationHandler : INotificationHandler<ParticipantsRoomChangedNotification>
    {
        private readonly IMediator _mediator;

        public ParticipantsRoomChangedNotificationHandler(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task Handle(ParticipantsRoomChangedNotification notification, CancellationToken cancellationToken)
        {
            await _mediator.Send(new UpdateParticipantsPermissionsRequest(notification.Participants
                .Where(x => !x.Value.HasLeft).Select(x => x.Key)));
        }
    }
}
