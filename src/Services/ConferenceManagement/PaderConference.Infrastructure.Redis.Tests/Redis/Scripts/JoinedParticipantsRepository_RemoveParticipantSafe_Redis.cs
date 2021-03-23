﻿using PaderConference.Infrastructure.Redis.Tests.Redis;
using PaderConference.Infrastructure.Tests.KeyValue.Scripts.Base;
using PaderConference.IntegrationTests._Helpers;
using Xunit;

namespace PaderConference.IntegrationTests.Infrastructure.Redis.Scripts
{
    public class JoinedParticipantsRepository_RemoveParticipantSafe_Redis :
        JoinedParticipantsRepository_RemoveParticipantSafe_Tests, IClassFixture<RedisDbConnector>
    {
        public JoinedParticipantsRepository_RemoveParticipantSafe_Redis(RedisDbConnector connector) : base(
            KeyValueDatabaseFactory.Create(connector.CreateConnection()))
        {
        }
    }
}
