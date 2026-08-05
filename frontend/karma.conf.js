// Karma configuration for `ng test`.
//
// When `karmaConfig` is set in angular.json the Angular builder supplies an empty
// base config, so everything the built-in config would have provided has to live
// here. Mirrors @angular/build's default, plus a CI launcher and lcov output.
module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
    ],
    jasmineHtmlReporter: {
      suppressAll: true, // removes the duplicated traces
    },
    reporters: ['progress', 'kjhtml'],
    browsers: ['Chrome'],

    customLaunchers: {
      // GitHub runners execute as root, where Chrome's sandbox cannot start, and
      // their /dev/shm is too small for Chrome's default shared-memory use.
      ChromeHeadlessCI: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--headless', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },

    coverageReporter: {
      dir: require('path').join(__dirname, './coverage'),
      subdir: '.',
      reporters: [{ type: 'html' }, { type: 'text-summary' }, { type: 'lcovonly' }],
      // Enforced floor, matching the backend's gate. Karma instruments only the files a
      // spec reaches, so covering a new area can move the ratio either way — see the
      // Frontend section of docs/COVERAGE_ROADMAP.md before changing these.
      check: {
        global: {
          statements: 90,
          lines: 90,
          branches: 90,
          functions: 90,
        },
      },
    },

    restartOnFileChange: true,
  });
};
