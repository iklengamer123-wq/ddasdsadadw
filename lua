local Rayfield = loadstring(game:HttpGet('https://sirius.menu/rayfield'))()

local Window = Rayfield:CreateWindow({
    Name = "[🌊🏎️] +1 Speed Go Kart Tsunami Escape / DLC",
    LoadingTitle = "Loading..",
    LoadingSubtitle = "by roseonwindow",
    ConfigurationSaving = {
       Enabled = true,
       FolderName = nil,
       FileName = "BigHub"
    },
    Discord = {
       Enabled = false,
       Invite = "noinvitelink",
       RememberJoins = true
    },
    KeySystem = false,
    KeySettings = {
       Title = "Untitled",
       Subtitle = "Key System",
       Note = "No method of obtaining the key is provided",
       FileName = "Key",
       SaveKey = true,
       GrabKeyFromSite = false,
       Key = {"Hello"}
    }
})

--// Vkl
local MainTab = Window:CreateTab("Main", nil)
local OtherTab = Window:CreateTab("Other", nil)

--// Logic TP
local function TeleportToCups()
    local character = game.Players.LocalPlayer.Character
    if character then
        local humanoid = character:FindFirstChildOfClass("Humanoid")
        local rootPart = character:FindFirstChild("HumanoidRootPart")
        if rootPart and humanoid then
            rootPart.CFrame = CFrame.new(10107.6, 96.6, -9.9)
            humanoid:ChangeState(Enum.HumanoidStateType.Jumping)
        end
    end
end

--// Button +100K Trophies
MainTab:CreateButton({
    Name = "+100K Trophies",
    Callback = TeleportToCups
})

--// Logic Unload
local function UnloadScript()
    Rayfield:Destroy()
end

--// Button Unload
OtherTab:CreateButton({
    Name = "Unload Script",
    Callback = UnloadScript
})
