var path = require("path");
var webpack = require("webpack");

var cfg = {
    entry: "../MetaApp.Server/bin/Debug/build/MetaApp.Client/Main.js",
    output: {
        path: path.join("../MetaApp.Server/bin/Debug", "public"),
        filename: "bundle.js"
    },
    module: {
        rules: [
          {
              test: /\.js$/,
              exclude: /node_modules/,
              loader: 'babel-loader',
          }
        ]
    },
    resolve: {
        modules: [
          "node_modules", path.resolve("./node_modules/")
        ]
    }
};


module.exports = cfg;