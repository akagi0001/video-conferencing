using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.SignalR.Client;
using Strive.Core;
using Strive.Core.Extensions;
using Strive.Core.Interfaces;
using Strive.Core.Services;
using Strive.Core.Services.Rooms;
using Strive.Core.Services.WhiteboardService;
using Strive.Core.Services.WhiteboardService.Actions;
using Strive.Core.Services.WhiteboardService.CanvasData;
using Strive.Core.Services.WhiteboardService.LiveActions;
using Strive.Core.Services.WhiteboardService.PushActions;
using Strive.Core.Services.WhiteboardService.Responses;
using Strive.Hubs.Core;
using Strive.Hubs.Core.Dtos;
using Strive.Hubs.Core.Responses;
using Strive.IntegrationTests._Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Strive.IntegrationTests.Services
{
    [Collection(IntegrationTestCollection.Definition)]
    public class WhiteboardTests : ServiceIntegrationTest
    {
        public WhiteboardTests(ITestOutputHelper testOutputHelper, MongoDbFixture mongoDb) : base(testOutputHelper,
            mongoDb)
        {
        }

        private async Task<string> CreateWhiteboard(UserConnection conn)
        {
            var result = await conn.Hub.InvokeAsync<SuccessOrError<string>>(nameof(CoreHub.CreateWhiteboard));
            AssertSuccess(result);

            return result.Response!;
        }

        private async Task<(string, CanvasLine)> CreateObjectLine(UserConnection conn, string whiteboardId)
        {
            var addedObj = new CanvasLine {X1 = 0, Y1 = 0, X2 = 5, Y2 = 5, StrokeWidth = 2, Stroke = "black"};

            var result = await conn.Hub.InvokeAsync<SuccessOrError<WhiteboardUpdatedResponse>>(
                nameof(CoreHub.WhiteboardPushAction),
                new WhiteboardPushActionDto(whiteboardId, new AddCanvasPushAction(addedObj)));

            // assert
            AssertSuccess(result);
            Assert.NotNull(result.Response);

            string? objId = null;
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    var canvas = whiteboard.Canvas;
                    objId = canvas.Objects.Single(x => x.Data.Equals(addedObj)).Id;

                    Assert.Equal(whiteboard.Version, result.Response!.Version);
                });

            Assert.NotNull(objId);
            return (objId!, addedObj);
        }

        private async Task SwitchToDifferentRoom(UserConnection conn)
        {
            var rooms = await conn.Hub.InvokeAsync<SuccessOrError<IReadOnlyList<Room>>>(nameof(CoreHub.CreateRooms),
                new List<RoomCreationInfo> {new("test")});
            AssertSuccess(rooms);

            var room = rooms.Response!.Single();

            var switchResult = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.SwitchRoom),
                new SwitchRoomDto(room.RoomId));
            AssertSuccess(switchResult);
        }

        [Fact]
        public async Task CreateWhiteboard_RoomNotExists_ReturnError()
        {
            // arrange
            var conference = await CreateConference(Moderator);
            var conn = await ConnectUserToConference(Moderator, conference);

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.CreateWhiteboard));

            // assert
            // assert
            AssertFailed(result);
            Assert.Equal(ConferenceError.RoomNotFound.Code, result.Error?.Code);
        }

        [Fact]
        public async Task CreateWhiteboard_RoomExists_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<string>>(nameof(CoreHub.CreateWhiteboard));

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    Assert.Equal(result.Response, Assert.Single(whiteboards.Whiteboards).Key);

                    var whiteboard = Assert.Single(whiteboards.Whiteboards).Value;
                    Assert.False(whiteboard.AnyoneCanEdit);
                    Assert.Empty(whiteboard.ParticipantStates);
                    Assert.Empty(whiteboard.Canvas.Objects);
                    Assert.NotEmpty(whiteboard.FriendlyName);
                });
        }

        [Fact]
        public async Task DeleteWhiteboard_WhiteboardExists_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            var syncObjId = SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID);

            await conn.SyncObjects.AssertSyncObject(syncObjId,
                (SynchronizedWhiteboards whiteboards) => Assert.Single(whiteboards.Whiteboards));

            // act
            var result =
                await conn.Hub.InvokeAsync<SuccessOrError<string>>(nameof(CoreHub.WhiteboardDelete), whiteboardId);

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(syncObjId,
                (SynchronizedWhiteboards whiteboards) => Assert.Empty(whiteboards.Whiteboards));
        }

        [Fact]
        public async Task DeleteWhiteboard_WhiteboardInDifferentRoom_Error()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            var syncObjId = SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID);
            await conn.SyncObjects.AssertSyncObject(syncObjId,
                (SynchronizedWhiteboards whiteboards) => Assert.Single(whiteboards.Whiteboards));

            await SwitchToDifferentRoom(conn);

            // act
            var result =
                await conn.Hub.InvokeAsync<SuccessOrError<string>>(nameof(CoreHub.WhiteboardDelete), whiteboardId);

            // assert
            AssertFailed(result);
            AssertErrorCode(ServiceErrorCode.Whiteboard_NotFound, result.Error!);
        }

        [Fact]
        public async Task UpdateSettings_WhiteboardExists_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            var syncObjId = SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID);

            await conn.SyncObjects.AssertSyncObject(syncObjId,
                (SynchronizedWhiteboards whiteboards) =>
                    Assert.False(whiteboards.Whiteboards.Single().Value.AnyoneCanEdit));

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardUpdateSettings),
                new WhiteboardUpdateSettingsDto(whiteboardId, new WhiteboardSettings(true)));

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(syncObjId,
                (SynchronizedWhiteboards whiteboards) =>
                    Assert.True(whiteboards.Whiteboards.Single().Value.AnyoneCanEdit));
        }

        [Fact]
        public async Task UpdateSettings_WhiteboardDoesNotExist_Error()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            await CreateWhiteboard(conn);

            var syncObjId = SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID);

            await conn.SyncObjects.AssertSyncObject(syncObjId,
                (SynchronizedWhiteboards whiteboards) =>
                    Assert.False(whiteboards.Whiteboards.Single().Value.AnyoneCanEdit));

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardUpdateSettings),
                new WhiteboardUpdateSettingsDto("wtf", new WhiteboardSettings(true)));

            // assert
            AssertFailed(result);
            AssertErrorCode(ServiceErrorCode.Whiteboard_NotFound, result.Error!);
        }

        [Fact]
        public async Task PushAction_CreateLine_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            // act
            var addedObj = new CanvasLine {X1 = 0, Y1 = 0, X2 = 5, Y2 = 5, StrokeWidth = 2, Stroke = "black"};

            var result = await conn.Hub.InvokeAsync<SuccessOrError<WhiteboardUpdatedResponse>>(
                nameof(CoreHub.WhiteboardPushAction),
                new WhiteboardPushActionDto(whiteboardId, new AddCanvasPushAction(addedObj)));

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    var obj = Assert.Single(whiteboard.Canvas.Objects);
                    Assert.NotEmpty(obj.Id);
                    Assert.Equal(whiteboard.Version, result.Response!.Version);
                    Assert.Equal(addedObj, obj.Data);
                });
        }

        [Fact]
        public async Task PushAction_UpdateLine_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            var (objId, _) = await CreateObjectLine(conn, whiteboardId);

            // act
            var patch = new JsonPatchDocument<CanvasObject>();
            patch.Add(x => x.ScaleX, 2);

            var canvasPatch = new CanvasObjectPatch(patch, objId);

            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardPushAction),
                new WhiteboardPushActionDto(whiteboardId, new UpdateCanvasPushAction(canvasPatch.Yield().ToList())));

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    var obj = Assert.Single(whiteboard.Canvas.Objects);
                    Assert.Equal(objId, obj.Id);
                    Assert.Equal(2, obj.Data.ScaleX);
                });
        }

        [Fact]
        public async Task PushAction_DeleteLine_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            var (objId, _) = await CreateObjectLine(conn, whiteboardId);

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardPushAction),
                new WhiteboardPushActionDto(whiteboardId, new DeleteCanvasPushAction(objId.Yield().ToList())));

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    Assert.Empty(whiteboard.Canvas.Objects);
                });
        }

        [Fact]
        public async Task PushAction_Pan_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardPushAction),
                new WhiteboardPushActionDto(whiteboardId, new PanCanvasPushAction(5, 0)));

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    Assert.Equal(5, whiteboard.Canvas.PanX);
                    Assert.Equal(0, whiteboard.Canvas.PanY);
                });
        }

        [Fact]
        public async Task PushAction_CreateLine_CanUndo()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            // act
            await CreateObjectLine(conn, whiteboardId);

            // assert
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    Assert.True(whiteboard.ParticipantStates[conn.User.Sub].CanUndo);
                });
        }

        [Fact]
        public async Task Undo_ActionPushed_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            await CreateObjectLine(conn, whiteboardId);

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardUndo), whiteboardId);

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    Assert.Empty(whiteboard.Canvas.Objects);
                    Assert.False(whiteboard.ParticipantStates[conn.User.Sub].CanUndo);
                    Assert.True(whiteboard.ParticipantStates[conn.User.Sub].CanRedo);
                });
        }

        [Fact]
        public async Task Undo_ActionNotPushed_ReturnError()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardUndo), whiteboardId);

            // assert
            AssertFailed(result);
            AssertErrorCode(ServiceErrorCode.Whiteboard_UndoNotAvailable, result.Error!);
        }

        [Fact]
        public async Task Redo_ActionPushed_UpdateSyncObj()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            await CreateObjectLine(conn, whiteboardId);
            AssertSuccess(
                await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardUndo), whiteboardId));

            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    Assert.True(whiteboard.ParticipantStates[conn.User.Sub].CanRedo);
                });

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardRedo), whiteboardId);

            // assert
            AssertSuccess(result);
            await conn.SyncObjects.AssertSyncObject(SynchronizedWhiteboards.SyncObjId(RoomOptions.DEFAULT_ROOM_ID),
                (SynchronizedWhiteboards whiteboards) =>
                {
                    var whiteboard = whiteboards.Whiteboards[whiteboardId];
                    Assert.NotEmpty(whiteboard.Canvas.Objects);
                    Assert.True(whiteboard.ParticipantStates[conn.User.Sub].CanUndo);
                    Assert.False(whiteboard.ParticipantStates[conn.User.Sub].CanRedo);
                });
        }

        [Fact]
        public async Task Redo_ActionNotPushed_ReturnError()
        {
            // arrange
            var (conn, _) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            // act
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardRedo), whiteboardId);

            // assert
            AssertFailed(result);
            AssertErrorCode(ServiceErrorCode.Whiteboard_RedoNotAvailable, result.Error!);
        }

        [Fact]
        public async Task WhiteboardLiveAction_Allowed_PublishToAll()
        {
            // arrange
            var (conn, conference) = await ConnectToOpenedConference();
            var whiteboardId = await CreateWhiteboard(conn);

            var olaf = CreateUser();
            var olafConnection = await ConnectUserToConference(olaf, conference);

            var notification = new TaskCompletionSource<WhiteboardLiveUpdateDto>();
            olafConnection.Hub.On(CoreHubMessages.OnWhiteboardLiveUpdate,
                (WhiteboardLiveUpdateDto dto) => notification.SetResult(dto));

            // act
            var liveAction = new DrawingLineCanvasLiveAction("black", 4, 0, 0, 5, 5);
            var result = await conn.Hub.InvokeAsync<SuccessOrError<Unit>>(nameof(CoreHub.WhiteboardLiveAction),
                new WhiteboardLiveActionDto(whiteboardId, liveAction));

            // assert
            AssertSuccess(result);

            var receivedUpdate = await notification.Task.WithDefaultTimeout();
            Assert.Equal(conn.User.Sub, receivedUpdate.ParticipantId);
            Assert.Equal(whiteboardId, receivedUpdate.WhiteboardId);
            Assert.Equal(liveAction, receivedUpdate.Action);
        }
    }
}
