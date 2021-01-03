﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PaderConference.Core.Domain.Entities;
using PaderConference.Core.Extensions;
using PaderConference.Core.Interfaces.Gateways.Repositories;
using PaderConference.Core.Interfaces.Services;

namespace PaderConference.Core.Services.Permissions
{
    /// <summary>
    ///     Provide and synchronize values from the database
    /// </summary>
    public class ConferenceConfigWatcher : IAsyncDisposable
    {
        private readonly string _conferenceId;
        private readonly IConferenceManager _conferenceManager;
        private readonly Func<IEnumerable<Participant>, ValueTask> _refreshParticipants;
        private readonly UseConferenceSelector<Conference> _conferenceSelector;

        public ConferenceConfigWatcher(string conferenceId, IConferenceRepo conferenceRepo,
            IConferenceManager conferenceManager, Func<IEnumerable<Participant>, ValueTask> refreshParticipants)
        {
            _conferenceSelector = new UseConferenceSelector<Conference>(conferenceId, conferenceRepo, x => x,
                new Conference(conferenceId), ReferenceEqualityComparer.Instance);

            _conferenceId = conferenceId;
            _conferenceManager = conferenceManager;
            _refreshParticipants = refreshParticipants;
        }

        /// <summary>
        ///     The current conference permissions
        /// </summary>
        public IImmutableDictionary<string, JValue>? ConferencePermissions { get; private set; }

        /// <summary>
        ///     The current moderator permissions
        /// </summary>
        public IImmutableDictionary<string, JValue>? ModeratorPermissions { get; private set; }

        /// <summary>
        ///     All current moderators
        /// </summary>
        public IImmutableList<string> Moderators { get; private set; } = ImmutableList<string>.Empty;

        public async ValueTask InitializeAsync()
        {
            await _conferenceSelector.InitializeAsync();
            _conferenceSelector.Updated += ConferenceSelectorOnUpdated;

            ConferencePermissions = GetPermissions(_conferenceSelector.Value.Permissions, PermissionType.Conference);
            ModeratorPermissions = GetPermissions(_conferenceSelector.Value.Permissions, PermissionType.Moderator);
            Moderators = _conferenceSelector.Value.Configuration.Moderators;
        }

        public ValueTask DisposeAsync()
        {
            return _conferenceSelector.DisposeAsync();
        }

        private async void ConferenceSelectorOnUpdated(object? sender, ObjectChangedEventArgs<Conference> e)
        {
            Moderators = _conferenceSelector.Value.Configuration.Moderators;

            var participants = _conferenceManager.GetParticipants(_conferenceId);
            var updatedParticipants = new HashSet<Participant>();

            // add all users that got their moderator state changed
            var updatedModerators = e.NewValue.Configuration.Moderators.Except(e.OldValue.Configuration.Moderators)
                .Concat(e.OldValue.Configuration.Moderators.Except(e.NewValue.Configuration.Moderators)).Distinct();

            updatedParticipants.UnionWith(updatedModerators
                .Select(x => participants.FirstOrDefault(p => p.ParticipantId == x)).WhereNotNull());

            var moderatorPermissions = GetPermissions(e.NewValue.Permissions, PermissionType.Moderator);
            if (!ComparePermissions(moderatorPermissions, ModeratorPermissions))
            {
                ModeratorPermissions = moderatorPermissions;
                updatedParticipants.UnionWith(e.NewValue.Configuration.Moderators
                    .Select(x => participants.FirstOrDefault(p => p.ParticipantId == x))
                    .WhereNotNull()); // add all current moderators
            }

            var conferencePermissions = GetPermissions(e.NewValue.Permissions, PermissionType.Conference);
            if (!ComparePermissions(conferencePermissions, ConferencePermissions))
            {
                ConferencePermissions = conferencePermissions;

                // add all participants of the conference
                updatedParticipants.UnionWith(participants);
            }

            if (updatedParticipants.Any())
                await _refreshParticipants(updatedParticipants);
        }

        /// <summary>
        ///     Utility method that compares two permission dictionaries
        /// </summary>
        /// <param name="source">The first dictionary</param>
        /// <param name="target">The second dictionary</param>
        /// <returns>Return true if the permission dictionaries are equal (equal keys and values), else return false</returns>
        private static bool ComparePermissions(IReadOnlyDictionary<string, JValue>? source,
            IReadOnlyDictionary<string, JValue>? target)
        {
            if (source == null && target == null) return true;
            if (source == null || target == null) return false;

            return source.EqualItems(target);
        }

        private static IImmutableDictionary<string, JValue>? GetPermissions(
            Dictionary<PermissionType, Dictionary<string, JValue>> permissions, PermissionType type)
        {
            if (permissions.TryGetValue(type, out var result))
                return result.ToImmutableDictionary();

            return null;
        }
    }
}
