// this is a script that will these projects 
// Fable.Remoting.Client
// MetaApp.Client

const path = require('path');
const fs = require('fs-extra');
const fable = require('fable-compiler');

const BUILD_DIR = "../MetaApp.Server/bin/Debug/build";

const PROJ_FILE = "../MetaApp.Client/Main.fsx";

const fableconfig = {
    "projFile": PROJ_FILE,
    "outDir": BUILD_DIR
};

const targets = {
    clean() {
        return fable.promisify(fs.remove, path.join(BUILD_DIR))
    },
    build() {
        return this.clean()
          .then(_ => fable.compile(fableconfig))
    }
}

targets[process.argv[2] || "build"]().catch(err => {
    console.log(err);
    process.exit(-1);
});