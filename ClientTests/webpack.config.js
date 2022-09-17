var path = require("path");

var babelOptions = {
  presets: [
    ["@babel/preset-env", {
      "modules": false,
      "useBuiltIns": "usage",
    }]
  ]
};

module.exports = function (evn, argv) {
  var mode = argv.mode || "development";
  var isProduction = mode === "production";
  console.log("Webpack mode: " + mode);

  return {
    mode: mode,
    devtool: isProduction ? false : "eval-source-map",
    entry: './src/Client.fsproj',
    output: {
      filename: 'bundle.js',
      path: path.join(__dirname, '..', 'Fable.Remoting.IntegrationTests', 'client-dist'),
    },
    devServer: {
      contentBase: path.join(__dirname, '..', 'Fable.Remoting.IntegrationTests', 'client-dist'),
      port: 8080, // where to the run dev-server
      proxy: {
        '/api/*': { // tell webpack-dev-server to re-route all requests from client to the server
          target: "http://localhost:5000",// assuming the suave server is hosted op port 8080
          changeOrigin: true
        },
        '/IAuthServer/*': {
          target: "http://localhost:5000",// assuming the suave server is hosted op port 8080
          changeOrigin: true
        }
      }
    },
    module: {
      rules: [
        {
          test: /\.fs(x|proj)?$/,
          use: "fable-loader"
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

}
