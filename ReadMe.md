## Overview

This is a more bare bones "command-line / API wrapper" version of PcmHammer, should be helpful for anyone wanting to write a program ontop of it without modifying PcmHammer's code directly

These tools currently support reading, writing, and data logging with some General Motors PCM's: P01, P04, P08, P10, P12, P59, 4 connector 98-02 Black Box and E54.
## Note: This is work in progress unstable build until stated otherwise and may contain bugs that the original PCM Hammer doesn't have.

## Download CMD PcmHammer

Bare bones PcmHammer CMD version: https://github.com/ToxicHax/PcmHammerCMD/releases

The most recent release will be at the top of that page once available.
Click "Assets" (below the description of the release) and download the .zip file.   
Extract the contents of the zip file, and you can access PcmHammerCMD.exe through a command line.

![Screenshot 1](screenshots/screenshot_0.png)
(ignore the double 'read_entire' under the example.. shhhh, i may have overlooked that)

## API Wrapper? Library?

Not yet.
The mentioned CMD PcmHammer Library will probably have a seperate GitHub since its not an actual library that requires PcmHammer code, its more of a standalone wrapper that uses pipe messages to send and interact with this PcmHammer CMD program.
once its available the GitHub and source for the DLL will be posted below.
For now im focusing on 1 project at a time.

## Links

[PcmHamer CMD Wiki](https://github.com/ToxicHax/PcmHammerCMD/wiki)

[Original PcmHammer GitHub Page](https://github.com/PcmHammer/PcmHammer)

##

## What purpose does the 'command-line / API wrapper' version of PcmHammer serve?

You could make your own .bin editing software and use this as the back bones to read and flash files via the api without having the users to switch between programs..etc, its up to the user to decide if they want to manually flash the .bin files but including this as a streamlined option it could be a nice feature for software creators to include as an option.
