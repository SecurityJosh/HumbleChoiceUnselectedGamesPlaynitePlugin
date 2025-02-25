# Humble Choice Unselected Games
This [Playnite](https://playnite.link) library add-on allows you to import your
unselected Humble Choice games. It is especially useful because Humble's UI requires you
to click through every month and search manually for any unredeemed choices. This add-on provides
a link to the game's redemption page, and saves the month and year of the Choice subscription
as the "Source".

This fork fixes a problem where the library would empty itself and re-populate all of its
contents every time a sync was run. This was problematic because other add-ons would then
be forced to download their metadata again. The process could add several minutes to Playnite's launch.

Additionally, this fork adds the option to import all unclaimed games/entitlements from your
Humble library. It also notifies you of any keys that will soon expire.

# Installation

Extract the zip you download from [Releases](https://github.com/Kortantic/HumbleChoiceUnselectedGamesPlaynitePlugin/releases)
into your Playnite Extensions folder. The default location is `C:\Users\[username]\AppData\Roaming\Playnite\Extensions\64ca9178-daac-44e7-a80b-0a33efaa9e6e`.
You may need to create the folder that contains the long string of letters, dashes, and numbers.

Currently, the only means of authentication with Humble's servers is to use your browser cookie.
You will need to fill in its value in library settings.

## Finding humblebundle.com _simpleauth_sess cookie value:
![image](https://github.com/user-attachments/assets/a30b6d46-293b-4440-911e-905beaa9aa94)

***Go to [humblebundle.com](https://humblebundle.com)***

Press *f-12* to open the developer's tools panel and go to the **Storage** tab on FireFox, or **Application** tab in Chrome

![image](https://github.com/user-attachments/assets/5d4fea68-c194-4931-bcf9-771f84d52567)

Open **Cookies** in the sidebar and click **https://humblebundle.com**

![image](https://github.com/user-attachments/assets/35457472-e118-47aa-ab72-89baeeec0ab6)

Find the **_simpleauth_sess** cookie using the filter, or by browsing the available cookie fields
![image](https://github.com/user-attachments/assets/17057b44-069e-4aac-8291-94bea365c4a5)

Copy its value, not the whole cookie, and paste it into this extension's settings.

# TODO
- [x] Import user's unredeemed non-choice keys
- [x] Only delete redeemed or expired keys on sync
- [ ] Allow user to authenticate with username and password instead of cookie
- [ ] Use Choice's release date and user's purchase date as "Date Added"
- [ ] Allow the user to configure when/if the library should warn of expiring keys
- [ ] Change the folder the extension installs into to one with a human-readable name
- [ ] Start using Steam `AppID`s instead of generating `GameId`s with the game's name to allow extensions to scrape Steam data
