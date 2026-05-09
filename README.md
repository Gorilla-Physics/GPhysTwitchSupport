# GPhys Twitch + Youtube Support
A mod that simply adds twitch and youtube support.

## How to Setup
- Install the addon either manually (Gorilla Tag\BepInEx\plugins\GPhysPlugins) or via the GPhys Addon Downloader from the PC menu (hold R to access, then go to addons)
- If you manually installed, start your game, wait for GPhys to fully load, close it, else if you installed it via the GPhys Addon Downloader, simply close your game.
- Now go to Gorilla Tag\BepInEx\config and edit GTwitch.txt
- Set it up with your settings, for Twitch go to https://twitchtokengenerator.com/ and generate a code, for Youtube go to https://console.cloud.google.com/, create a new project or select existing, enable "YouTube Data API v3", Go to Credentials -> Create Credentials -> API Key, and then copy the API key, then when your starting a live stream, start the stream first, and then copy the video id (after "v=") and paste it into youtube_live_id.
- Now you can start your game, i'd recommend having a list of commands at the bottom of the screen to inform users on how to use it. (Mine is "Commands; !spawnobject "name", !killplayer, !damageplayer 0-200, !healplayer 0-200, !canister "fast, poison, classic, or baby", 1-inf, !explosion, !cleanup")
- If your using Twitch, start streaming and it should work, else with Youtube you should already be streaming since you set it up with the live id.
- Done!

## Commands
!spawnobject "name"      
!killplayer      
!damageplayer 0-200      
!healplayer 0-200 (doesn't work sometimes)      
!canister "fast, poison, classic, or baby" 1-2,147,483,647      
!explosion      
!cleanup      
