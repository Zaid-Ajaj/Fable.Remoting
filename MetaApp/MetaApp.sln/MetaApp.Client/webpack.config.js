var path = require("path");
var webpack = require("webpack");

function resolve(filePath) {
  return path.join(__dirname, filePath)
}

var babelOptions = {
  presets: [["es2015", {"modules": false}]],
  plugins: ["transform-runtime"]
}




module.exports = {
  devtool: "source-map",
  entry: resolve('./MetaApp.Client.fsproj'),
  output: {
    filename: 'bundle.js',
    path: resolve('./public'),
  },
  devServer: {
    contentBase: resolve('./public'),
    proxy: {
      // for developement, redirect all calls to 
      // the suave host
      '/': {
        target: 'http://localhost:8083',
        changeOrigin: true,
        // bypass the proxy when asking fot the home page
        bypass: function(req, res, proxyOptions) {
          if (req.headers.accept.indexOf('html') !== -1) {
            console.log('Skipping proxy for browser request.');
            return '/index.html';
        }
      }
    }
    }
 },
  module: {
    rules: [
      {
        test: /\.fs(x|proj)?$/,
        use: {
          loader: "fable-loader",
          options: { babel: babelOptions }
        }
      },
      {
        test: /\.js$/,
        exclude: /node_modules[\\\/](?!fable-)/,
        use: {
          loader: 'babel-loader',
          options: babelOptions
        },
      }
    ]
  },
   resolve: {
      // do not resolve symbolic links
      symlinks: false,
      modules: [
      // Fix the relative path if node_modules is not in the same dir as webpack.config.js
      "node_modules", resolve("./node_modules/"),
      
      ]
  },
};