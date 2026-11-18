---
kind: fixed
---
Stop pages from waiting up to a minute for VRChat while the server is syncing

## Release Note
Pages no longer wait behind a sync
The server held every request to VRChat to 60 a minute, a pace the original VRCX only uses for
refreshing the friend list. During a sync, opening a profile or an instance waited in line
behind it. Requests are now paced exactly as the original paces its own.
