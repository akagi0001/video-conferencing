import { Redis } from 'ioredis';
import { Router } from 'mediasoup/lib/types';
import { LoopbackManager } from './loopback-manager';
import * as redisKeys from './pader-conference/redis-keys';
import { Participant, ProducerSource } from './participant';
import Room from './room';
import { ISignalWrapper } from './signal-wrapper';

const DEFAULT_ROOM_SOURCES: ProducerSource[] = ['mic', 'webcam', 'screen'];

/**
 * The room manager opens and closes media rooms and moves participant to the correct room
 */
export class RoomManager {
   private participantToRoomKey: string;

   constructor(conferenceId: string, private signal: ISignalWrapper, private router: Router, private redis: Redis) {
      this.participantToRoomKey = redisKeys.participantToRoom(conferenceId);
      this.loopbackManager = new LoopbackManager(signal, router, redis);
   }

   private loopbackManager: LoopbackManager;

   /** roomId -> Room */
   private roomMap: Map<string, Room> = new Map();

   /** participantId -> roomId */
   private participantToRoom = new Map<string, string>();

   public async updateParticipant(participant: Participant): Promise<void> {
      // loopback is independent from from the room
      this.loopbackManager.updateParticipant(participant);

      const roomId = await this.getParticipantRoom(participant.participantId);
      if (!roomId) return;

      // get the room or create a new one
      let room = this.roomMap.get(roomId);
      if (!room) {
         room = new Room(roomId, this.signal, this.router, this.redis, DEFAULT_ROOM_SOURCES);
         this.roomMap.set(roomId, room);
      }

      const currentRoomId = this.participantToRoom.get(participant.participantId);
      if (currentRoomId && roomId !== currentRoomId) {
         // room switch, remove from current room
         const currentRoom = this.roomMap.get(currentRoomId);
         if (currentRoom) {
            await currentRoom.leave(participant);

            if (currentRoom.participants.size === 0) {
               this.closeRoom(currentRoom);
            }
         }
      }

      if (roomId !== currentRoomId) {
         // join the new room
         await room.join(participant);
         this.participantToRoom.set(participant.participantId, roomId);
      } else {
         // just update the participant in the room
         await room.updateParticipant(participant);
      }
   }

   public async removeParticipant(participant: Participant): Promise<void> {
      const roomId = await this.getParticipantRoom(participant.participantId);
      if (!roomId) return;

      const room = this.roomMap.get(roomId);
      if (room) {
         await room.leave(participant);
         this.participantToRoom.delete(participant.participantId);

         if (room.participants.size === 0) {
            this.closeRoom(room);
         }
      }
   }

   private closeRoom(room: Room) {
      this.roomMap.delete(room.id);
   }

   private async getParticipantRoom(participantId: string): Promise<string | null> {
      return await this.redis.hget(this.participantToRoomKey, participantId);
   }
}
