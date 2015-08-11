# Headless client for the Second Life protocol.
Or more precisely, it has no GUI, but speaks a small subset of IRC as its user interface.

Currently, it listens on localhost:6668 and is hardwired for the main Second Life grid.

SL usernames are translated to IRC nicknames as Firstname.Lastname, so you'll see a lot of people
called things like "Bob.Resident".

Log in using your SL name thusly as your nick, and your SL password as the server password. Note
that error handling is sub-par so errors might not be sent to your client (this includes the one
where you tried to log in too soon after logging out or crashing).

## Supported:

 * Logging in
 * Talking and listening in local chat
 * Group chat
   * People with moderator permissions are listed as chanops.
   * You can't leave or detach a groupchat.
 * See who's in the same region as you.
   * People in regular chat range are voiced.
 * IMs
 * Friend presence, using [`MONITOR`](http://ircv3.net/specs/core/monitor-3.2.html) to report/
   display it.
   * You can't add or remove people, only see their presence.
   * You need a client that sensibly handles `730` and `731` for nicks it hasn't previously sent
     `MONITOR +` for.
 * RLV `@sit`. `@redirchat` sometimes works.

## Things that definitely don't work
 * Getting a correct list of who's in a groupchat with you (regular clients can't do this either)
 * Closing groupchats (you'll be forcibly rejoined)
 * Adding or removing friends
 * Joining or leaving groups
 * Pretty much everything else.

## Building
You'll probably need to correct the references to Libopenmetaverse, and you'll need to supply an
openmetaverse_data directory (put it next to the .exe) or your appearance may not bake (at least on
OpenSim grids. SL seems to be fine.).