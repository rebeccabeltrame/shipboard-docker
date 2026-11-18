// Under Aspire the API address is injected as services__api__http__0.
const target = process.env['services__api__http__0'] || 'http://localhost:5080';

module.exports = {
  '/api': {
    target,
    pathRewrite: { '^/api': '' },
    changeOrigin: true,
  },
};
