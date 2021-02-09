import { Badge, Box, Button, Fab, List, makeStyles, Typography, Zoom } from '@material-ui/core';
import KeyboardArrowDownIcon from '@material-ui/icons/KeyboardArrowDown';
import _ from 'lodash';
import React, { useEffect, useRef, useState } from 'react';
import { DomainError } from 'src/communication-types';
import { ParticipantDto } from 'src/features/conference/types';
import useBottomScrollTrigger from 'src/hooks/useBottomScrollTrigger';
import { getScrollbarWidth } from 'src/utils/browser-info';
import { ChatMessageDto } from '../types';
import ChatMessage from './ChatMessage';

const useStyles = makeStyles({
   root: {
      height: '100%',
      position: 'relative',
   },
   list: {
      overflowY: 'scroll',
      height: '100%',
      display: 'flex',
      flexDirection: 'column-reverse',
   },
   scrollDownFab: {
      position: 'absolute',
      bottom: 8,
   },
});

type Props = {
   chat: ChatMessageDto[] | null;
   participants: ParticipantDto[] | null;
   participantId?: string;
   participantColors: { [id: string]: string };
   error: DomainError | null;
   onRetry: () => void;
};

export default function ChatMessageList({
   chat,
   participants,
   participantId,
   participantColors,
   error,
   onRetry,
}: Props) {
   const classes = useStyles();

   const listRef = useRef<HTMLOListElement>(null);
   const bottomAnchor = useRef<HTMLDivElement>(null);

   const [missedMessages, setMissedMessages] = useState(0);

   const atBottom = useBottomScrollTrigger({
      target: listRef.current ?? undefined,
      disableHysteresis: true,
      threshold: 25,
   });

   useEffect(() => {
      if (atBottom) setMissedMessages(0);
   }, [atBottom]);

   useEffect(() => {
      if (!atBottom) {
         setMissedMessages((x) => x + 1);
      }
   }, [chat]);

   const handleScrollToBottom = (behavior: 'auto' | 'smooth' = 'smooth') => {
      if (bottomAnchor.current) {
         bottomAnchor.current.scrollIntoView({ behavior, block: 'center' });
      }
   };

   return (
      <div className={classes.root}>
         <List className={classes.list} ref={listRef}>
            <div ref={bottomAnchor} />
            {chat == null ? (
               error ? (
                  <Box m={2} display="flex" flexDirection="column" alignItems="center">
                     <Typography
                        color="error"
                        gutterBottom
                     >{`An error occurred when trying to fetch the chat: ${error.message}`}</Typography>
                     <Button onClick={onRetry} variant="contained">
                        Retry
                     </Button>
                  </Box>
               ) : (
                  Array.from({ length: 8 }).map((_, i) => <ChatMessage key={i} participantColors={participantColors} />)
               )
            ) : (
               _([...chat])
                  .reverse()
                  .map((x) => (
                     <ChatMessage
                        key={x.messageId}
                        participantColors={participantColors}
                        participants={participants}
                        message={x}
                        participantId={participantId}
                     />
                  ))
                  .value()
            )}
         </List>
         <Zoom in={!atBottom}>
            <Fab
               color="primary"
               size="small"
               aria-label="scroll back down"
               className={classes.scrollDownFab}
               onClick={() => handleScrollToBottom()}
               style={{ right: 8 + getScrollbarWidth() }}
            >
               <Badge badgeContent={missedMessages} color="secondary">
                  <KeyboardArrowDownIcon />
               </Badge>
            </Fab>
         </Zoom>
      </div>
   );
}