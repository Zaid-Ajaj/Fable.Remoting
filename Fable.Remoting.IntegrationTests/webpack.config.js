var path = require("path");
var webpack = require("webpack");
var fableUtils = require("fable-utils");

function resolve(filePath) {
  return path.join(__dirname, filePath)
}

var babelOptions = fableUtils.resolveBabelOptions({
  presets: [["es2015", { "modules": false }]],
  plugins: [["transform-runtime", {
      "helpers": true,
      // We don't need the polyfills as we're already calling
      // cdn.polyfill.io/v2/polyfill.js in index.html
      "polyfill": false,
      "regenerator": false
  }]]
});

var isProduction = process.argv.indexOf("-p") >= 0;
console.log("Bundling for " + (isProduction ? "production" : "development") + "...");

module.exports = {
  devtool: "source-map",
  entry: resolve('./Client/IntegrationTests.Client.fsproj'),
  output: {
    filename: 'bundle.js',
    path: resolve('./Server/client'),
  },
  resolve: {
    modules: [resolve("./node_modules/")]
  },
  devServer: {
    contentBase: resolve('./Server/client'),
    port: 8081,
    proxy: {
      '/api/*': { // tell webpack-dev-server to re-route all requests from client to the server
        target: "http://localhost:8080",// assuming the suave server is hosted op port 8083
        changeOrigin: true
    }
  },
  },
  module: {
    rules: [
      {
        test: /\.fs(x|proj)?$/,
        use: {
          loader: "fable-loader",
          options: {
            babel: babelOptions,
            define: isProduction ? [] : ["DEBUG"]
          }
        }
      },
      {
        test: /\.js$/,
        exclude: /node_modules/,
        use: {
          loader: 'babel-loader',
          options: babelOptions
        },
      }
    ]
  }
}
