# Avatar ID Generator

A workaround to upload new avatar in old VRChat Avatar SDK (< 3.9.0). 

## Why

In case you're having trouble in newer SDK, or you are insisting to stick to old version.

[A recent update](https://x.com/VRChat/status/1999588790038597715) requires to generate the avatar id on server-side, which breaks the features to create new avatar with older SDK (updating exist avatars is not affected), since they're not capable for the new pipeline. Instead, after all the build and upload process, you'll see an error message says "Id is not allowed to be used., Make sure you're using SDK 3.9.0 or newer.".

## Usage

After install the package, navigate to `Tools` -> `Avatar ID Generator`, drag the avatar object into the selector, click the BIG BUTTON, and now you can upload it with old SDK.

> Remember to click the "Save Changes" button in SDK Builder if you change any fields or upload the thumbnail.

## Principle

The newer SDK (>= 3.9.0) will request the server to create an avatar first, then assign the returned id to the Blueprint ID in Pipeline Manager.

So the tool is just requesting the API like the newer SDK, then get and set the id, then you're able to upload the new avatar.

## I don't want to use the tool, any old school ways?

1. Create an empty project with the latest SDK.
2. Open the new project in Unity.
3. Import any avatar or just use `Packages\com.vrchat.avatars\Samples\Dynamics\Robot Avatar`.
4. Navigate to SDK's Builder, fill the form, then click the VERY BIG BUTTON.
5. Ignore the Copyright ownership agreement popup, copy the Blueprint ID from the avatar's Pipeline Manager component.

## Known issues

- The tool is Chinese only for now, but I believe you can do it, just click around then you're good to go.

- The new avatar isn't assigned a valid thumbnail, which may have some affects like can't use the new avatar or failed to load in the client, so it's recommend to update the thumbnail before uploading or after uploading or any time before you use it. Besides, the issue also exists in the SDK itself.

## License

CC0 or WTFPL for the source code under `Packages\com.ccloli.vrchat.avatar-id-generator`, since Gemini investigated the new implementation and wrote 95% of the code (though I took 95% of the time to debug and fix the credential stuff). So if it doesn't work, don't blame me, blame Gemini.
