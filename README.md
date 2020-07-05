# Derpibooru Archive Scraper

Scrapes images from [The Pony Archive](https://www.theponyarchive.com/) that have a specific tag.

## Setup

This app requires setting up a [PostgreSQL database](https://www.enterprisedb.com/downloads/postgres-postgresql-downloads) hosting a data dump from Derpibooru. These can be found on the [Data Dumps](https://derpibooru.org/pages/data_dumps) page, along with instructions on how to set it up.

Key notes:

- If on Windows, the commands will likely not be found after installing. This can be fixed by updating the system path to point to the install directory (`C:\Program Files\PostgreSQL\12\bin`), or calling the command with the full path (in powershell: `& "C:\Program Files\PostgreSQL\12\bin\createdb.exe" -U postgres derpibooru`). Note the `&` if on Powershell. That is needed.
- If you don't care, set the root password to `password`. The program will already use that password to log in. If set to anything else, the connection string must be modified in the code.

## Building

The app is built using .NET Core 2.2 with Visual Studio 2019. Earlier versions may work but are not tested.
