# Headless client for the Second Life protocol.
Or more precisely, it has no GUI, but speaks a small subset of IRC as its user interface.

Currently, it listens on localhost:6668 and is hardwired for the main Second Life grid.

SL usernames are translated to IRC nicknames as Firstname.Lastname, so you'll see a lot of people
called things like "Bob.Resident".

Log in using your SL name thusly as your nick, and your SL password as the server password. Note
that error handling is sub-par so errors might not be sent to your client (this includes the one
where you tried to log in too soon after logging out or crashing).

Supported:

 * Logging in
 * Talking and listening in local chat
 * Listening in group chat
 * Maybe talking in group chat
 * Radar enough to see who's in the same region as you.
 * IMs

Everything else probably won't work.

## Building
You'll probably need to correct the references to Libopenmetaverse, and you'll need to supply an
openmetaverse_data directory (put it next to the .exe) or your appearance may not bake.