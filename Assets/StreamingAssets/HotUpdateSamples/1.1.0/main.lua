-- Sample contract only. A real Lua VM adapter must provide the restricted api.
local balance = require("config.combat")

return {
    bootstrap = function(api)
        api.log("nebula hot-update 1.1.0")
        return balance
    end
}

